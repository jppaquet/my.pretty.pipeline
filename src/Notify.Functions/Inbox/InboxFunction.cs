using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Inbox;

// Thin HTTP shim around InboxHandler. AuthorizationLevel.Function uses the
// Function App's per-function key — same model as RegisterDeviceFunction
// because the inbox is user-owned, not project-scoped.
public sealed class InboxFunction
{
    private readonly InboxHandler _handler;
    private readonly ILogger<InboxFunction> _logger;

    public InboxFunction(InboxHandler handler, ILogger<InboxFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Inbox")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/inbox")]
        HttpRequestData req)
    {
        var query = HttpUtility.ParseQueryString(req.Url.Query);
        var request = new InboxQueryRequest
        {
            Source = query["source"],
            Limit = int.TryParse(query["limit"], out var l) ? l : InboxOptions.DefaultLimit,
            ContinuationToken = query["continuationToken"],
        };

        var result = await _handler.HandleAsync(request);

        return result switch
        {
            InboxResult.Ok ok           => await Json(req, HttpStatusCode.OK, new { items = ok.Items, continuationToken = ok.ContinuationToken }),
            InboxResult.BadRequest bad  => await Json(req, HttpStatusCode.BadRequest, new { errors = bad.Failures }),
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
