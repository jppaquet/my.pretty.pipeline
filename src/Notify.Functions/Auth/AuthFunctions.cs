using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Notify.Shared.Json;

namespace Notify.Functions.Auth;

// Exchanges an Apple Sign-in identity token for a Notify session JWT. This is
// the only place the Apple JWT is validated; every other protected endpoint
// trusts the session token validated by `JwtAuthMiddleware`. The route is
// path-bypassed in the middleware (no Bearer expected on the way in).
//
// Flow on POST /v1/auth/session:
//   1. Read `{ "identityToken": "..." }` from the body.
//   2. Validate the Apple JWT (issuer, audience, expiry, signature).
//   3. Allowlist gate (same repository the middleware uses).
//   4. Mint a session JWT and return `{ sessionToken, expiresAt }`.
public sealed class AuthFunctions
{
    private readonly AppleJwtValidator _apple;
    private readonly IAllowlistRepository _allowlist;
    private readonly SessionTokenIssuer _issuer;
    private readonly ILogger<AuthFunctions> _logger;

    public AuthFunctions(
        AppleJwtValidator apple,
        IAllowlistRepository allowlist,
        SessionTokenIssuer issuer,
        ILogger<AuthFunctions> logger)
    {
        _apple = apple;
        _allowlist = allowlist;
        _issuer = issuer;
        _logger = logger;
    }

    [Function("CreateSession")]
    public async Task<HttpResponseData> CreateSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/auth/session")]
        HttpRequestData req,
        FunctionContext context)
    {
        SessionRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SessionRequest>(req.Body, NotifyJson.Options, context.CancellationToken);
        }
        catch (JsonException)
        {
            return await Text(req, HttpStatusCode.BadRequest, "malformed request body");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.IdentityToken))
            return await Text(req, HttpStatusCode.BadRequest, "identityToken required");

        var user = await _apple.ValidateAsync(body.IdentityToken, context.CancellationToken);
        if (user is null)
        {
            _logger.LogWarning("rejected /v1/auth/session: invalid Apple identity token");
            return await Text(req, HttpStatusCode.Unauthorized, "invalid identity token");
        }

        var approved = await _allowlist.IsApprovedAsync(user.Sub, context.CancellationToken);
        if (!approved)
        {
            _logger.LogWarning("rejected /v1/auth/session for sub {Sub}: awaiting approval", user.Sub);
            return await Text(req, HttpStatusCode.Forbidden, "user awaiting approval");
        }

        var issued = _issuer.Issue(user.Sub);
        return await Json(req, HttpStatusCode.OK, new SessionResponse(issued.Token, issued.ExpiresAt));
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(body, NotifyJson.Options));
        return resp;
    }

    private static async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode status, string body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("content-type", "text/plain; charset=utf-8");
        await resp.WriteStringAsync(body);
        return resp;
    }

    public sealed record SessionRequest([property: JsonPropertyName("identityToken")] string IdentityToken);
    public sealed record SessionResponse(
        [property: JsonPropertyName("sessionToken")] string SessionToken,
        [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt);
}
