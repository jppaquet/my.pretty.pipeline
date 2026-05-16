using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Notify.Functions.Auth;

// Worker middleware that runs on every function invocation. For HTTP requests
// it looks for an `Authorization: Bearer …` header; if present, validates the
// JWT through AppleJwtValidator. Behavior:
//   - Absent header     → pass through (function-key gate handles it)
//   - Present + valid   → attach AppleUser to FunctionContext.Items["AppleUser"]
//                          and proceed (handlers read it via this key)
//   - Present + invalid → return 401 immediately (don't invoke the handler)
//
// Non-HTTP invocations (EventGrid triggers for Archive/Push) are a no-op.
//
// In PR-A this is purely additive: nothing requires a JWT yet, so existing
// function-key clients are unaffected. PR-B starts populating the header from
// the iOS app; PR-C flips Inbox + RegisterDevice to require the AppleUser.
public sealed class JwtAuthMiddleware : IFunctionsWorkerMiddleware
{
    public const string UserContextKey = "AppleUser";

    private readonly ILogger<JwtAuthMiddleware> _logger;

    public JwtAuthMiddleware(ILogger<JwtAuthMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = await context.GetHttpRequestDataAsync();
        if (http is null)
        {
            await next(context);
            return;
        }

        if (!TryExtractBearer(http, out var token))
        {
            await next(context);
            return;
        }

        var validator = context.InstanceServices.GetRequiredService<AppleJwtValidator>();
        var user = await validator.ValidateAsync(token, context.CancellationToken);
        if (user is null)
        {
            _logger.LogWarning("rejected request with invalid Bearer token on {Function}", context.FunctionDefinition.Name);
            var resp = http.CreateResponse(HttpStatusCode.Unauthorized);
            resp.Headers.Add("content-type", "text/plain; charset=utf-8");
            await resp.WriteStringAsync("invalid bearer token");
            context.GetInvocationResult().Value = resp;
            return;
        }

        context.Items[UserContextKey] = user;
        await next(context);
    }

    private static bool TryExtractBearer(HttpRequestData http, out string token)
    {
        token = "";
        if (!http.Headers.TryGetValues("Authorization", out var values))
            return false;
        var raw = values.FirstOrDefault();
        if (raw is null || !raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = raw["Bearer ".Length..].Trim();
        if (candidate.Length == 0)
            return false;
        token = candidate;
        return true;
    }
}
