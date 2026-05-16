using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Notify.Functions.Auth;

namespace Notify.Auth.Tests;

// AppleJwtValidator is the security boundary for every authenticated client
// request. These tests exercise it with a synthetic JWKS so we can vary
// claims and key state without touching Apple's live service.
public class AppleJwtValidatorTests
{
    private const string Audience = "my.pretty.pipeline";
    private const string Issuer = AppleJwtValidator.AppleIssuer;

    [Fact]
    public async Task Valid_token_returns_user_with_sub()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "001234.abcdef", iss: Issuer, aud: Audience, expiresIn: TimeSpan.FromMinutes(10));

        var user = await validator.ValidateAsync(token, Audience, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("001234.abcdef", user!.Sub);
    }

    [Fact]
    public async Task Wrong_issuer_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: "https://accounts.google.com", aud: Audience, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, Audience, CancellationToken.None));
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: "other.bundle.id", expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, Audience, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        // Backdate the whole token: issued 2h ago, valid for 1h → expired 1h ago,
        // well past the 5-minute clock skew tolerance.
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: Audience,
            issuedAt: DateTime.UtcNow.AddHours(-2), expiresIn: TimeSpan.FromHours(1));

        Assert.Null(await validator.ValidateAsync(token, Audience, CancellationToken.None));
    }

    [Fact]
    public async Task Signature_from_unknown_key_is_rejected()
    {
        using var signing = RSA.Create(2048);
        using var trusted = RSA.Create(2048);
        // Validator trusts `trusted` only; token is signed with `signing`.
        var (validator, _) = BuildValidator(trusted);
        var token = MintToken(signing, sub: "u", iss: Issuer, aud: Audience, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, Audience, CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_token_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);

        Assert.Null(await validator.ValidateAsync("not.a.jwt", Audience, CancellationToken.None));
        Assert.Null(await validator.ValidateAsync("", Audience, CancellationToken.None));
        Assert.Null(await validator.ValidateAsync("   ", Audience, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_audience_config_rejects_everything()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: Audience, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, "", CancellationToken.None));
    }

    [Fact]
    public async Task Default_options_audience_is_used_when_not_overridden()
    {
        using var rsa = RSA.Create(2048);
        var (validator, _) = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: Audience, expiresIn: TimeSpan.FromMinutes(10));

        var user = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("u", user!.Sub);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static (AppleJwtValidator validator, FakeJwksProvider jwks) BuildValidator(RSA rsa)
    {
        var jwks = new FakeJwksProvider(rsa);
        var opts = Options.Create(new AuthOptions { AppleAudience = Audience });
        return (new AppleJwtValidator(jwks, opts), jwks);
    }

    private static string MintToken(RSA rsa, string sub, string iss, string aud, TimeSpan expiresIn, DateTime? issuedAt = null)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key-1" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var issued = issuedAt ?? DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: iss,
            audience: aud,
            subject: new ClaimsIdentity(new[] { new Claim("sub", sub) }),
            notBefore: issued,
            expires: issued.Add(expiresIn),
            issuedAt: issued,
            signingCredentials: creds);
        return handler.WriteToken(token);
    }

    private sealed class FakeJwksProvider : IAppleJwksProvider
    {
        private readonly SecurityKey _key;
        public FakeJwksProvider(RSA rsa) => _key = new RsaSecurityKey(rsa) { KeyId = "test-key-1" };
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<SecurityKey>>(new[] { _key });
    }
}
