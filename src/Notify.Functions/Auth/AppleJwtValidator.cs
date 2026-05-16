using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Auth;

// Validates a Sign-in-with-Apple identity token (JWT) against Apple's JWKS.
// On success returns the validated `AppleUser`; on any failure returns null
// — callers decide how to react (401 on a required gate, ignore on an
// additive gate). The validator never throws on a malformed/expired token;
// authentication failures are part of the normal request flow.
//
// Validation rules (matches Apple's documented contract):
//   - iss = "https://appleid.apple.com"
//   - aud = configured app bundle id (AuthOptions.AppleAudience)
//   - exp present, not expired (5 min clock skew)
//   - signature verifies against a key in Apple's JWKS
public sealed class AppleJwtValidator
{
    public const string AppleIssuer = "https://appleid.apple.com";

    private readonly IAppleJwksProvider _jwks;
    private readonly IOptions<AuthOptions> _opts;
    private readonly JwtSecurityTokenHandler _handler;

    public AppleJwtValidator(IAppleJwksProvider jwks, IOptions<AuthOptions> opts)
    {
        _jwks = jwks;
        _opts = opts;
        // JwtSecurityTokenHandler's default inbound claim type map silently
        // renames `sub` → `ClaimTypes.NameIdentifier`. We want claims preserved
        // as-issued so `principal.FindFirst("sub")` works.
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    public Task<AppleUser?> ValidateAsync(string bearerToken, CancellationToken ct)
        => ValidateAsync(bearerToken, _opts.Value.AppleAudience, ct);

    // Exposed for unit tests that want to override the audience without DI.
    public async Task<AppleUser?> ValidateAsync(string bearerToken, string expectedAudience, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(expectedAudience))
            return null;
        if (!_handler.CanReadToken(bearerToken))
            return null;

        IReadOnlyCollection<SecurityKey> keys;
        try
        {
            keys = await _jwks.GetSigningKeysAsync(ct);
        }
        catch (HttpRequestException)
        {
            // Apple JWKS unreachable — fail closed. The middleware will return
            // 401 if this was the only credential supplied.
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidateIssuer = true,
            ValidAudience = expectedAudience,
            ValidateAudience = true,
            IssuerSigningKeys = keys,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        try
        {
            var principal = _handler.ValidateToken(bearerToken, parameters, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return string.IsNullOrWhiteSpace(sub) ? null : new AppleUser(sub);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            // ArgumentException + FormatException land here when the bearer is
            // syntactically a JWT (3 dot-separated segments) but a segment
            // fails base64-url decode. `CanReadToken` accepts that shape, so
            // the validator is the first place to catch it.
            return null;
        }
    }
}
