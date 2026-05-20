using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Auth;

// Mints HS256 session JWTs after a successful Sign-in-with-Apple exchange.
// The session token is what the iOS app sends on every subsequent request;
// `SessionTokenValidator` verifies it with the same secret. Default lifetime
// is 30d (configurable). The signing key lives in Key Vault and is read at
// startup via `AuthOptions.SessionSigningKey`; rotating it invalidates every
// active session, which is the only revocation mechanism by design.
public sealed class SessionTokenIssuer
{
    private readonly IOptions<AuthOptions> _opts;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly TimeProvider _clock;

    public SessionTokenIssuer(IOptions<AuthOptions> opts, TimeProvider? clock = null)
    {
        _opts = opts;
        _clock = clock ?? TimeProvider.System;
    }

    public IssuedSession Issue(string appleSub)
    {
        if (string.IsNullOrWhiteSpace(appleSub))
            throw new ArgumentException("apple sub required", nameof(appleSub));

        var opts = _opts.Value;
        if (string.IsNullOrWhiteSpace(opts.SessionSigningKey))
            throw new InvalidOperationException("Auth__SessionSigningKey is not configured");
        if (string.IsNullOrWhiteSpace(opts.AppleAudience))
            throw new InvalidOperationException("Auth__AppleAudience is not configured");

        var keyBytes = Encoding.UTF8.GetBytes(opts.SessionSigningKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Auth__SessionSigningKey must be ≥ 32 bytes of entropy");

        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.Add(opts.SessionLifetime);

        var token = _handler.CreateJwtSecurityToken(
            issuer: opts.SessionIssuer,
            audience: opts.AppleAudience,
            subject: new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, appleSub) }),
            notBefore: now,
            expires: expires,
            issuedAt: now,
            signingCredentials: creds);

        return new IssuedSession(_handler.WriteToken(token), expires);
    }
}

public readonly record struct IssuedSession(string Token, DateTime ExpiresAt);
