using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Functions.Auth;
using Notify.Shared.Json;

namespace Notify.Functions.Inbox;

// Per-user inbox-row mutations: mark-as-read + soft delete (hide).
// Same `AuthorizationLevel.Anonymous` + JWT-middleware pattern as InboxFunction;
// the handler enforces that the row's userId suffix matches the validated sub.
//
// `source` is the partition key for the Cosmos doc; it comes in as a query
// param mirroring GET /v1/inbox?source=. The `id` is the full doc id
// (`{baseId}:{userId}`) the client received from the inbox response.
public sealed class InboxMutationFunction
{
    private readonly InboxMutationHandler _handler;
    private readonly ILogger<InboxMutationFunction> _logger;

    public InboxMutationFunction(InboxMutationHandler handler, ILogger<InboxMutationFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("InboxMarkRead")]
    public Task<HttpResponseData> MarkRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/inbox/{id}/read")]
        HttpRequestData req,
        FunctionContext context,
        string id)
        => RunAsync(req, context, id, InboxMutationHandler.Action.MarkRead);

    [Function("InboxDelete")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/inbox/{id}")]
        HttpRequestData req,
        FunctionContext context,
        string id)
        => RunAsync(req, context, id, InboxMutationHandler.Action.MarkHidden);

    private async Task<HttpResponseData> RunAsync(
        HttpRequestData req,
        FunctionContext context,
        string id,
        InboxMutationHandler.Action action)
    {
        if (!context.Items.TryGetValue(JwtAuthMiddleware.UserContextKey, out var raw)
            || raw is not AppleUser user)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            unauth.Headers.Add("content-type", "text/plain; charset=utf-8");
            await unauth.WriteStringAsync("missing or invalid bearer token");
            return unauth;
        }

        // Percent-only parser — see Inbox/QueryStringParser.cs. Source
        // names follow [A-Za-z0-9._-] today so this is defensive, but
        // mirroring InboxFunction's parsing keeps the two paths in sync.
        var source = QueryStringParser.Parse(req.Url.Query).GetValueOrDefault("source");
        var result = await _handler.HandleAsync(user.Sub, source, id, action);

        return result switch
        {
            InboxMutationResult.NoContent => req.CreateResponse(HttpStatusCode.NoContent),
            InboxMutationResult.BadRequest bad => await Json(req, HttpStatusCode.BadRequest, new { errors = bad.Failures }),
            InboxMutationResult.Unauthorized => req.CreateResponse(HttpStatusCode.Unauthorized),
            InboxMutationResult.Forbidden => req.CreateResponse(HttpStatusCode.Forbidden),
            InboxMutationResult.NotFound => req.CreateResponse(HttpStatusCode.NotFound),
            _ => throw new InvalidOperationException($"Unexpected result type {result.GetType()}"),
        };
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(body, NotifyJson.Options));
        return resp;
    }
}
