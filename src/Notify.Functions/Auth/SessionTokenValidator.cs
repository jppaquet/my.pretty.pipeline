using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Auth;

// Validates a Notify session JWT minted by `SessionTokenIssuer`. Same shape
// as `AppleJwtValidator`: returns the `AppleUser` (per-user partition key)
// on success, null on any failure. The middleware uses this — Apple tokens
// are only validated at the `/v1/auth/session` exchange.
public sealed class SessionTokenValidator
{
    private readonly IOptions<AuthOptions> _opts;
    private readonly JwtSecurityTokenHandler _handler;

    public SessionTokenValidator(IOptions<AuthOptions> opts)
    {
        _opts = opts;
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    public AppleUser? Validate(string bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return null;
        if (!_handler.CanReadToken(bearerToken))
            return null;

        var opts = _opts.Value;
        if (string.IsNullOrWhiteSpace(opts.SessionSigningKey)
            || string.IsNullOrWhiteSpace(opts.AppleAudience)
            || string.IsNullOrWhiteSpace(opts.SessionIssuer))
            return null;

        var keyBytes = Encoding.UTF8.GetBytes(opts.SessionSigningKey);
        if (keyBytes.Length < 32)
            return null;

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = opts.SessionIssuer,
            ValidateIssuer = true,
            ValidAudience = opts.AppleAudience,
            ValidateAudience = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        };

        try
        {
            var principal = _handler.ValidateToken(bearerToken, parameters, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return string.IsNullOrWhiteSpace(sub) ? null : new AppleUser(sub);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
