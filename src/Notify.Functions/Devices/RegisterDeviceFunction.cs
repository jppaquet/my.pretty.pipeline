using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Functions.Auth;
using Notify.Functions.Devices;
using Notify.Shared.Json;

namespace Notify.Functions.Devices;

// Thin HTTP shim around RegisterHandler. `AuthorizationLevel.Anonymous` here
// means the Functions host does not enforce a function-key check — auth is
// the JWT, validated by JwtAuthMiddleware and required by this function:
// without an AppleUser in the request context we return 401. The handler
// receives the validated Sub and binds the registered device to it so push
// fan-out + inbox filtering can target the right recipient.
public sealed class RegisterDeviceFunction
{
    private readonly RegisterHandler _handler;
    private readonly ILogger<RegisterDeviceFunction> _logger;

    public RegisterDeviceFunction(RegisterHandler handler, ILogger<RegisterDeviceFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("RegisterDevice")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/devices")]
        HttpRequestData req,
        FunctionContext context)
    {
        if (!(context.Items.TryGetValue(JwtAuthMiddleware.UserContextKey, out var raw) && raw is AppleUser user))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            unauth.Headers.Add("content-type", "text/plain; charset=utf-8");
            await unauth.WriteStringAsync("missing or invalid bearer token");
            return unauth;
        }

        var contentLength = req.Headers.TryGetValues("content-length", out var clen) && long.TryParse(clen.FirstOrDefault(), out var cl) ? cl : (long?)null;

        var result = await _handler.HandleAsync(user.Sub, req.Body, contentLength);

        return result switch
        {
            RegisterResult.Accepted a    => await Json(req, HttpStatusCode.Accepted, new { installationId = a.InstallationId }),
            RegisterResult.BadRequest b  => await Json(req, HttpStatusCode.BadRequest, new { errors = b.Failures }),
            RegisterResult.PayloadTooLarge p => await Json(req, HttpStatusCode.RequestEntityTooLarge, new { error = $"payload exceeds {p.LimitBytes} bytes" }),
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
