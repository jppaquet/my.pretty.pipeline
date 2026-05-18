using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Admin;

// /admin/projects endpoints — producer key mint + list + revoke. The mint
// response is the *only* place the cleartext `npk_…` is ever surfaced; the
// caller (the admin SPA) must show it to the operator immediately and the
// operator must store it themselves. Backend never persists it.
public sealed class ProjectsAdminFunction
{
    private readonly ProjectsAdminHandler _handler;
    private readonly ILogger<ProjectsAdminFunction> _logger;

    public ProjectsAdminFunction(ProjectsAdminHandler handler, ILogger<ProjectsAdminFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("AdminListProjects")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/projects")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var items = await _handler.ListAsync(ctx.CancellationToken);
        _logger.LogInformation("admin {Actor} listed {Count} projects", Actor(ctx), items.Count);
        return await Json(req, HttpStatusCode.OK, new { items });
    }

    [Function("AdminMintProject")]
    public async Task<HttpResponseData> Mint(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/projects")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        MintRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<MintRequest>(req.Body, NotifyJson.Options, ctx.CancellationToken);
        }
        catch (JsonException ex)
        {
            return await Json(req, HttpStatusCode.BadRequest, new { error = $"invalid JSON: {ex.Message}" });
        }
        if (body is null)
            return await Json(req, HttpStatusCode.BadRequest, new { error = "missing body" });

        var result = await _handler.MintAsync(body.ProjectId, body.DisplayName, ctx.CancellationToken);
        return result switch
        {
            ProjectMutationResult.OkWithKey ok => await OnMinted(req, ctx, ok),
            ProjectMutationResult.AlreadyExists => await Json(req, HttpStatusCode.Conflict, new { error = "project already exists" }),
            ProjectMutationResult.InvalidInput inv => await Json(req, HttpStatusCode.BadRequest, new { errors = new[] { new { field = inv.Field, message = inv.Message } } }),
            _ => throw new InvalidOperationException($"unexpected mint result {result.GetType().Name}"),
        };
    }

    [Function("AdminRevokeProject")]
    public async Task<HttpResponseData> Revoke(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/projects/{id}/revoke")]
        HttpRequestData req,
        FunctionContext ctx,
        string id)
    {
        var result = await _handler.RevokeAsync(id, ctx.CancellationToken);
        return result switch
        {
            ProjectMutationResult.Ok ok => await OnRevoked(req, ctx, id, ok),
            ProjectMutationResult.NotFound => req.CreateResponse(HttpStatusCode.NotFound),
            ProjectMutationResult.InvalidInput inv => await Json(req, HttpStatusCode.BadRequest, new { errors = new[] { new { field = inv.Field, message = inv.Message } } }),
            _ => throw new InvalidOperationException($"unexpected revoke result {result.GetType().Name}"),
        };
    }

    private async Task<HttpResponseData> OnMinted(HttpRequestData req, FunctionContext ctx, ProjectMutationResult.OkWithKey ok)
    {
        _logger.LogInformation("admin {Actor} minted project {ProjectId}", Actor(ctx), ok.Project.Id);
        // Never log the cleartext key. The response body is the single
        // surface where it exists outside the operator's clipboard.
        return await Json(req, HttpStatusCode.Created, new { project = ok.Project, key = ok.PlaintextKey });
    }

    private async Task<HttpResponseData> OnRevoked(HttpRequestData req, FunctionContext ctx, string id, ProjectMutationResult.Ok ok)
    {
        _logger.LogInformation("admin {Actor} revoked project {ProjectId}", Actor(ctx), id);
        return await Json(req, HttpStatusCode.OK, ok.Project);
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

    private sealed record MintRequest(string ProjectId, string DisplayName);
}
