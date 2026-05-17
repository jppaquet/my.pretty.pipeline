using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Notify.Functions.Admin;

namespace Notify.Auth.Tests;

// EntraJwtValidator is the security boundary for the admin plane. Mirrors
// AppleJwtValidatorTests structurally — synthetic JWKS so we can vary
// claims and key state without touching Entra.
public class EntraJwtValidatorTests
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";
    private const string Audience = "11111111-1111-1111-1111-111111111111";
    private const string AdminRole = "Admin";
    private static readonly string Issuer = string.Format(EntraJwtValidator.IssuerFormat, TenantId);

    [Fact]
    public async Task Valid_token_with_admin_role_returns_user()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "user-1", iss: Issuer, aud: Audience, roles: new[] { AdminRole }, expiresIn: TimeSpan.FromMinutes(10));

        var user = await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("user-1", user!.Sub);
    }

    [Fact]
    public async Task Token_without_required_role_is_rejected()
    {
        // Signed-in tenant member who *doesn't* hold the Admin role should
        // look indistinguishable from an invalid token — we don't expose
        // "you authenticated but lack the role" because it would let a
        // valid-token holder probe whether they're an admin.
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "user-2", iss: Issuer, aud: Audience, roles: Array.Empty<string>(), expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Token_with_unrelated_roles_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "user-3", iss: Issuer, aud: Audience, roles: new[] { "Reader", "Editor" }, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Wrong_tenant_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var otherIssuer = string.Format(EntraJwtValidator.IssuerFormat, "99999999-9999-9999-9999-999999999999");
        var token = MintToken(rsa, sub: "u", iss: otherIssuer, aud: Audience, roles: new[] { AdminRole }, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: "wrong-app-id", roles: new[] { AdminRole }, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: Audience, roles: new[] { AdminRole },
            issuedAt: DateTime.UtcNow.AddHours(-2), expiresIn: TimeSpan.FromHours(1));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Signature_from_unknown_key_is_rejected()
    {
        using var signing = RSA.Create(2048);
        using var trusted = RSA.Create(2048);
        var validator = BuildValidator(trusted);
        var token = MintToken(signing, sub: "u", iss: Issuer, aud: Audience, roles: new[] { AdminRole }, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, AdminRole, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_config_rejects_everything()
    {
        using var rsa = RSA.Create(2048);
        var validator = BuildValidator(rsa);
        var token = MintToken(rsa, sub: "u", iss: Issuer, aud: Audience, roles: new[] { AdminRole }, expiresIn: TimeSpan.FromMinutes(10));

        Assert.Null(await validator.ValidateAsync(token, "", Audience, AdminRole, CancellationToken.None));
        Assert.Null(await validator.ValidateAsync(token, TenantId, "", AdminRole, CancellationToken.None));
        Assert.Null(await validator.ValidateAsync(token, TenantId, Audience, "", CancellationToken.None));
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static EntraJwtValidator BuildValidator(RSA rsa)
    {
        var jwks = new FakeEntraJwksProvider(rsa);
        var opts = Options.Create(new AdminOptions
        {
            EntraTenantId = TenantId,
            EntraAudience = Audience,
            RequiredRole = AdminRole,
        });
        return new EntraJwtValidator(jwks, opts);
    }

    private static string MintToken(RSA rsa, string sub, string iss, string aud, IReadOnlyCollection<string> roles, TimeSpan expiresIn, DateTime? issuedAt = null)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key-1" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var issued = issuedAt ?? DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", sub) };
        foreach (var r in roles) claims.Add(new Claim("roles", r));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: iss,
            audience: aud,
            subject: new ClaimsIdentity(claims),
            notBefore: issued,
            expires: issued.Add(expiresIn),
            issuedAt: issued,
            signingCredentials: creds);
        return handler.WriteToken(token);
    }

    private sealed class FakeEntraJwksProvider : IEntraJwksProvider
    {
        private readonly SecurityKey _key;
        public FakeEntraJwksProvider(RSA rsa) => _key = new RsaSecurityKey(rsa) { KeyId = "test-key-1" };
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<SecurityKey>>(new[] { _key });
    }
}
