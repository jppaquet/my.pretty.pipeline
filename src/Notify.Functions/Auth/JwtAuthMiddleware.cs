using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Notify.Functions.Auth;

// Worker middleware that runs on every function invocation. For HTTP requests
// it looks for an `Authorization: Bearer …` header; if present, validates the
// JWT through AppleJwtValidator and consults IAllowlistRepository:
//   - Absent header     → pass through (function-key gate handles it)
//   - Present + invalid → 401, don't invoke the handler
//   - Present + valid + approved
//                       → attach AppleUser to FunctionContext.Items["AppleUser"]
//                          and proceed (handlers read it via this key)
//   - Present + valid + not approved
//                       → 403 "awaiting approval". The repository will have
//                         upserted a pending row so the sub appears in Cosmos
//                         Data Explorer for the admin to flip.
//
// When AuthOptions.CosmosAllowedUsersContainer is unset, Program.cs binds the
// AlwaysApproveAllowlistRepository — preserves pre-allowlist behavior.
//
// Non-HTTP invocations (EventGrid triggers for Archive/Push) are a no-op.
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

        // /v1/admin/* requests carry Entra tokens, not Apple. AdminAuthMiddleware
        // owns that path; this middleware passes through without trying to
        // validate the Bearer header as a Sign-in-with-Apple JWT (which would
        // 401 every legitimate admin request). Path matches the route prefix
        // declared on AdminAuthMiddleware — kept under /v1/admin/ rather than
        // bare /admin/ because the host reserves /admin/* for its own API.
        if (http.Url.AbsolutePath.StartsWith("/v1/admin/", StringComparison.OrdinalIgnoreCase))
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

        var allowlist = context.InstanceServices.GetRequiredService<IAllowlistRepository>();
        var approved = await allowlist.IsApprovedAsync(user.Sub, context.CancellationToken);
        if (!approved)
        {
            _logger.LogWarning("rejected sign-in for sub {Sub} on {Function}: awaiting approval in allowedUsers container", user.Sub, context.FunctionDefinition.Name);
            var resp = http.CreateResponse(HttpStatusCode.Forbidden);
            resp.Headers.Add("content-type", "text/plain; charset=utf-8");
            await resp.WriteStringAsync("user awaiting approval");
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
