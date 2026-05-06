using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.DeviceApi.Devices;
using Notify.Shared.Json;

namespace Notify.DeviceApi.Functions;

// Thin HTTP shim around RegisterHandler. AuthorizationLevel.Function uses the
// Function App's per-function key — the iOS app fetches it once via TestFlight
// build configuration. Project-key auth (npk_*) is not used here because
// devices are user-owned, not project-scoped.
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
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/devices")]
        HttpRequestData req)
    {
        var contentLength = req.Headers.TryGetValues("content-length", out var clen) && long.TryParse(clen.FirstOrDefault(), out var cl) ? cl : (long?)null;

        var result = await _handler.HandleAsync(req.Body, contentLength);

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
