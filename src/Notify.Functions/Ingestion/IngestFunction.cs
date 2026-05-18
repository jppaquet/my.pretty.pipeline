using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Ingestion;

// Thin HTTP shim around IngestHandler. Owns content-type dispatch and CE parsing;
// the handler is purely auth + validation + publish.
//
// Accepts CloudEvents 1.0 in three modes per the HTTP binding spec:
//   - structured single:   Content-Type: application/cloudevents+json
//   - batch (structured):  Content-Type: application/cloudevents-batch+json
//   - binary single:       ce-* attribute headers + application/json body
// Anything else returns 415.
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
        var apiKey = req.Headers.TryGetValues("x-api-key", out var keys) ? keys.FirstOrDefault() : null;
        var contentType = req.Headers.TryGetValues("content-type", out var cts) ? cts.FirstOrDefault() : null;
        var contentLength = req.Headers.TryGetValues("content-length", out var clen)
            && long.TryParse(clen.FirstOrDefault(), out var cl) ? cl : (long?)null;

        if (contentLength is > IngestionOptions.MaxRequestBodyBytes)
            return await Json(req, HttpStatusCode.RequestEntityTooLarge,
                new { error = $"payload exceeds {IngestionOptions.MaxRequestBodyBytes} bytes" });

        BinaryData body;
        try
        {
            body = await ReadBoundedAsync(req.Body, IngestionOptions.MaxRequestBodyBytes);
        }
        catch (PayloadTooLargeException)
        {
            return await Json(req, HttpStatusCode.RequestEntityTooLarge,
                new { error = $"payload exceeds {IngestionOptions.MaxRequestBodyBytes} bytes" });
        }

        bool isBatch;
        IReadOnlyList<IngestInput>? events;
        string? parseError;

        if (contentType is not null
            && contentType.StartsWith(CloudEventsParser.BatchContentType, StringComparison.OrdinalIgnoreCase))
        {
            isBatch = true;
            if (!CloudEventsParser.TryParseBatch(body, out events, out parseError))
                return await BadParse(req, parseError);
        }
        else if (contentType is not null
            && contentType.StartsWith(CloudEventsParser.StructuredContentType, StringComparison.OrdinalIgnoreCase))
        {
            isBatch = false;
            if (!CloudEventsParser.TryParseStructured(body, out var input, out parseError))
                return await BadParse(req, parseError);
            events = new[] { input };
        }
        else if (req.Headers.TryGetValues("ce-specversion", out _))
        {
            isBatch = false;
            if (!CloudEventsParser.TryParseBinary(GetHeader, contentType, body, out var input, out parseError))
                return await BadParse(req, parseError);
            events = new[] { input };
        }
        else
        {
            _logger.LogInformation("Ingest rejected: unsupported Content-Type '{ContentType}'", contentType);
            return await Json(req, HttpStatusCode.UnsupportedMediaType, new
            {
                error = "expected CloudEvents 1.0 — one of: "
                      + $"'{CloudEventsParser.StructuredContentType}', "
                      + $"'{CloudEventsParser.BatchContentType}', "
                      + "or binary mode with ce-specversion/ce-type/ce-source/ce-id headers.",
            });
        }

        var result = await _handler.HandleAsync(apiKey, events, isBatch);

        return result switch
        {
            IngestResult.Accepted a       => await Json(req, HttpStatusCode.Accepted, new { id = a.Id }),
            IngestResult.AcceptedBatch ab => await Json(req, HttpStatusCode.Accepted, new { ids = ab.Ids }),
            IngestResult.BadRequest b     => await Json(req, HttpStatusCode.BadRequest, new { errors = b.Failures }),
            IngestResult.Unauthorized     => Empty(req, HttpStatusCode.Unauthorized),
            IngestResult.PayloadTooLarge p => await Json(req, HttpStatusCode.RequestEntityTooLarge, new { error = $"payload exceeds {p.LimitBytes} bytes" }),
            IngestResult.UnsupportedMediaType u => await Json(req, HttpStatusCode.UnsupportedMediaType, new { error = u.Message }),
            _ => throw new InvalidOperationException($"Unexpected result type {result.GetType()}"),
        };

        string? GetHeader(string name) =>
            req.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
    }

    private static async Task<HttpResponseData> BadParse(HttpRequestData req, string? error)
        => await Json(req, HttpStatusCode.BadRequest, new
        {
            errors = new[] { new { field = "body", message = error ?? "parse error" } },
        });

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(body, NotifyJson.Options));
        return resp;
    }

    private static HttpResponseData Empty(HttpRequestData req, HttpStatusCode status) => req.CreateResponse(status);

    // Bounded body read: stop and signal at the limit so we never buffer more
    // than `limitBytes` for a hostile producer that lies about content-length
    // (or omits it).
    private static async Task<BinaryData> ReadBoundedAsync(Stream body, int limitBytes)
    {
        using var ms = new MemoryStream();
        var buf = new byte[8 * 1024];
        var total = 0;
        int read;
        while ((read = await body.ReadAsync(buf)) > 0)
        {
            total += read;
            if (total > limitBytes) throw new PayloadTooLargeException();
            ms.Write(buf, 0, read);
        }
        return new BinaryData(ms.ToArray());
    }

    private sealed class PayloadTooLargeException : Exception { }
}
