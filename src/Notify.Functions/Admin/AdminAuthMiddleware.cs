using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Notify.Functions.Admin;

// Sibling of [[JwtAuthMiddleware]] for the admin plane. Runs on every
// invocation; only acts when the request path starts with `/admin/`. Logic:
//   - Non-admin route        → pass through (the Apple-side middleware
//                              handles the iOS plane)
//   - Admin route + no token → 401
//   - Admin route + bad/missing-role token → 401 (same response — don't leak
//                              "the token was valid but lacked the role")
//   - Admin route + valid    → attach AdminUser to Items["AdminUser"]
//
// Disabled-by-default: if AdminOptions.EntraTenantId is empty we respond
// 503 on /admin/* — pre-Entra-bootstrap forks can't accidentally expose an
// open admin surface.
public sealed class AdminAuthMiddleware : IFunctionsWorkerMiddleware
{
    public const string UserContextKey = "AdminUser";
    // Under `/v1/admin/`, not bare `/admin/` — the Functions host reserves
    // `/admin/*` for its own runtime administration API (/admin/host/status,
    // /admin/functions/<name>, /admin/vfs/<path>) and intercepts requests
    // before any HttpTrigger gets routed. Using `/v1/admin/` keeps us in the
    // same `/v1/` family as the iOS-facing routes and out of host territory.
    public const string RoutePrefix = "/v1/admin/";

    private readonly ILogger<AdminAuthMiddleware> _logger;
    private readonly IOptions<AdminOptions> _opts;

    public AdminAuthMiddleware(ILogger<AdminAuthMiddleware> logger, IOptions<AdminOptions> opts)
    {
        _logger = logger;
        _opts = opts;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = await context.GetHttpRequestDataAsync();
        if (http is null || !IsAdminRoute(http))
        {
            await next(context);
            return;
        }

        var opts = _opts.Value;
        if (string.IsNullOrWhiteSpace(opts.EntraTenantId) || string.IsNullOrWhiteSpace(opts.EntraAudience))
        {
            _logger.LogWarning("admin route hit but AdminOptions is not configured; returning 503");
            await WritePlain(context, http, HttpStatusCode.ServiceUnavailable, "admin plane not configured");
            return;
        }

        if (!TryExtractBearer(http, out var token))
        {
            await WritePlain(context, http, HttpStatusCode.Unauthorized, "missing bearer token");
            return;
        }

        var validator = context.InstanceServices.GetRequiredService<EntraJwtValidator>();
        var user = await validator.ValidateAsync(token, context.CancellationToken);
        if (user is null)
        {
            _logger.LogWarning("rejected admin request on {Function}: token invalid or missing role", context.FunctionDefinition.Name);
            await WritePlain(context, http, HttpStatusCode.Unauthorized, "invalid bearer token");
            return;
        }

        context.Items[UserContextKey] = user;
        await next(context);
    }

    private static bool IsAdminRoute(HttpRequestData http)
        => http.Url.AbsolutePath.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase);

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

    private static async Task WritePlain(FunctionContext context, HttpRequestData http, HttpStatusCode status, string body)
    {
        var resp = http.CreateResponse(status);
        resp.Headers.Add("content-type", "text/plain; charset=utf-8");
        await resp.WriteStringAsync(body);
        context.GetInvocationResult().Value = resp;
    }
}
