using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Admin;

// /admin/allowlist endpoints. AdminAuthMiddleware gates every call; if we
// reach a handler an Entra-validated Admin is on the line and
// Items["AdminUser"] is set. Mutating endpoints return the row's new state
// so the SPA can update its view without re-listing.
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
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/allowlist")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var items = await _handler.ListAsync(ctx.CancellationToken);
        _logger.LogInformation("admin {Actor} listed {Count} allowlist rows", Actor(ctx), items.Count);
        return await Json(req, HttpStatusCode.OK, new { items });
    }

    [Function("AdminApproveAllowlist")]
    public async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/allowlist/{sub}/approve")]
        HttpRequestData req,
        FunctionContext ctx,
        string sub)
    {
        var doc = await _handler.ApproveAsync(sub, ctx.CancellationToken);
        if (doc is null)
        {
            _logger.LogWarning("admin {Actor} tried to approve missing sub {Sub}", Actor(ctx), sub);
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
        _logger.LogInformation("admin {Actor} approved sub {Sub}", Actor(ctx), sub);
        return await Json(req, HttpStatusCode.OK, doc);
    }

    [Function("AdminRevokeAllowlist")]
    public async Task<HttpResponseData> Revoke(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/allowlist/{sub}/revoke")]
        HttpRequestData req,
        FunctionContext ctx,
        string sub)
    {
        var doc = await _handler.RevokeAsync(sub, ctx.CancellationToken);
        if (doc is null)
        {
            _logger.LogWarning("admin {Actor} tried to revoke missing sub {Sub}", Actor(ctx), sub);
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
        _logger.LogInformation("admin {Actor} revoked sub {Sub}", Actor(ctx), sub);
        return await Json(req, HttpStatusCode.OK, doc);
    }

    private static string Actor(FunctionContext ctx)
        => ctx.Items.TryGetValue(AdminAuthMiddleware.UserContextKey, out var u) && u is AdminUser au
            ? au.PreferredUsername : "<unknown>";

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(body, NotifyJson.Options));
        return resp;
    }
}
