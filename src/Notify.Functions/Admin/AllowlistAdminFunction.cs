using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Admin;

// GET /admin/allowlist — returns every row in `allowedUsers` (pending +
// approved). AdminAuthMiddleware gates access; if we reach this handler an
// Entra-validated Admin is on the line and Items["AdminUser"] is set.
public sealed class AllowlistAdminFunction
{
    private readonly AllowlistAdminHandler _handler;
    private readonly ILogger<AllowlistAdminFunction> _logger;

    public AllowlistAdminFunction(AllowlistAdminHandler handler, ILogger<AllowlistAdminFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("AdminListAllowlist")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/allowlist")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var actor = ctx.Items.TryGetValue(AdminAuthMiddleware.UserContextKey, out var u) && u is AdminUser au
            ? au.PreferredUsername : "<unknown>";
        var items = await _handler.ListAsync(ctx.CancellationToken);
        _logger.LogInformation("admin {Actor} listed {Count} allowlist rows", actor, items.Count);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(new { items }, NotifyJson.Options));
        return resp;
    }
}
