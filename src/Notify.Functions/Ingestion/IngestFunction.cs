using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Functions.Ingestion;
using Notify.Shared.Json;

namespace Notify.Functions.Ingestion;

// Thin HTTP shim around IngestHandler. Maps headers + body in, IngestResult out.
public sealed class IngestFunction
{
    private readonly IngestHandler _handler;
    private readonly ILogger<IngestFunction> _logger;

    public IngestFunction(IngestHandler handler, ILogger<IngestFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Ingest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/notifications")]
        HttpRequestData req)
    {
        var apiKey = req.Headers.TryGetValues("x-api-key", out var values) ? values.FirstOrDefault() : null;
        var contentLength = req.Headers.TryGetValues("content-length", out var clen) && long.TryParse(clen.FirstOrDefault(), out var cl) ? cl : (long?)null;

        var result = await _handler.HandleAsync(apiKey, req.Body, contentLength);

        return result switch
        {
            IngestResult.Accepted a   => await Json(req, HttpStatusCode.Accepted, new { id = a.Id }),
            IngestResult.BadRequest b => await Json(req, HttpStatusCode.BadRequest, new { errors = b.Failures }),
            IngestResult.Unauthorized => Empty(req, HttpStatusCode.Unauthorized),
            IngestResult.PayloadTooLarge p => await Json(req, HttpStatusCode.RequestEntityTooLarge, new { error = $"payload exceeds {p.LimitBytes} bytes" }),
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

    private static HttpResponseData Empty(HttpRequestData req, HttpStatusCode status) => req.CreateResponse(status);
}
