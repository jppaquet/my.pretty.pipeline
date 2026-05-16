using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Functions.Auth;
using Notify.Shared.Json;

namespace Notify.Functions.Inbox;

// Thin HTTP shim around InboxHandler. `AuthorizationLevel.Anonymous` here
// means the Functions host does not enforce a function-key check — auth is
// the JWT, validated by JwtAuthMiddleware and required by this function:
// without an AppleUser in the request context we return 401. The handler
// receives the validated Sub and uses it as the partition for the inbox
// query; a token holder can only see their own notifications.
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/inbox")]
        HttpRequestData req,
        FunctionContext context)
    {
        if (context.Items.TryGetValue(JwtAuthMiddleware.UserContextKey, out var raw)
            && raw is AppleUser user)
        {
            var query = HttpUtility.ParseQueryString(req.Url.Query);
            var request = new InboxQueryRequest
            {
                Source = query["source"],
                Limit = int.TryParse(query["limit"], out var l) ? l : InboxOptions.DefaultLimit,
                ContinuationToken = query["continuationToken"],
            };

            var result = await _handler.HandleAsync(user.Sub, request);

            return result switch
            {
                InboxResult.Ok ok           => await Json(req, HttpStatusCode.OK, new { items = ok.Items, continuationToken = ok.ContinuationToken }),
                InboxResult.BadRequest bad  => await Json(req, HttpStatusCode.BadRequest, new { errors = bad.Failures }),
                _ => throw new InvalidOperationException($"Unexpected result type {result.GetType()}"),
            };
        }

        var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
        unauth.Headers.Add("content-type", "text/plain; charset=utf-8");
        await unauth.WriteStringAsync("missing or invalid bearer token");
        return unauth;
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(body, NotifyJson.Options));
        return resp;
    }
}
