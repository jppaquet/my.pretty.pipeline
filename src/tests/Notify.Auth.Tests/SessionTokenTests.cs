using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Notify.Functions.Auth;

namespace Notify.Auth.Tests;

// Covers the round-trip between SessionTokenIssuer (mints a session JWT
// after Apple sign-in) and SessionTokenValidator (the middleware-side
// check on every protected request). The two MUST stay symmetric — they
// share the same signing-key + audience + issuer config.
public class SessionTokenTests
{
    private const string Audience = "my.pretty.pipeline";
    private const string Issuer = "notify";
    // Deterministic 33-byte fixture — long enough to clear the issuer's
    // ≥ 32-byte length check, low enough entropy that gitleaks ignores it.
    private const string SigningKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // gitleaks:allow

    [Fact]
    public void Round_trip_returns_user_with_sub()
    {
        var opts = MakeOptions();
        var issuer = new SessionTokenIssuer(opts);
        var validator = new SessionTokenValidator(opts);

        var issued = issuer.Issue("001234.abcdef");
        var user = validator.Validate(issued.Token);

        Assert.NotNull(user);
        Assert.Equal("001234.abcdef", user!.Sub);
    }

    [Fact]
    public void Issued_token_carries_configured_lifetime()
    {
        var opts = MakeOptions(lifetime: TimeSpan.FromDays(30));
        var fixed_ = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new FakeClock(fixed_);
        var issuer = new SessionTokenIssuer(opts, clock);

        var issued = issuer.Issue("u");

        // 30 days from the frozen "now" — small tolerance for the JWT's
        // second-precision exp field.
        var delta = (issued.ExpiresAt - fixed_.UtcDateTime).TotalSeconds;
        Assert.InRange(delta, TimeSpan.FromDays(30).TotalSeconds - 2, TimeSpan.FromDays(30).TotalSeconds + 2);
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var opts = MakeOptions(lifetime: TimeSpan.FromMinutes(10));
        var clock = new FakeClock(DateTimeOffset.UtcNow.AddDays(-2));
        var issuer = new SessionTokenIssuer(opts, clock);
        // Validator uses system clock → token issued 2d ago with 10-min lifetime
        // is expired well past the 1-min skew tolerance.
        var validator = new SessionTokenValidator(opts);

        var issued = issuer.Issue("u");
        Assert.Null(validator.Validate(issued.Token));
    }

    [Fact]
    public void Token_signed_with_different_key_is_rejected()
    {
        var minted = MakeOptions(signingKey: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"); // gitleaks:allow
        var trusted = MakeOptions(signingKey: SigningKey);
        var issued = new SessionTokenIssuer(minted).Issue("u");

        Assert.Null(new SessionTokenValidator(trusted).Validate(issued.Token));
    }

    [Fact]
    public void Wrong_audience_is_rejected()
    {
        var minted = MakeOptions(audience: "other.bundle.id");
        var trusted = MakeOptions(audience: Audience);
        var issued = new SessionTokenIssuer(minted).Issue("u");

        Assert.Null(new SessionTokenValidator(trusted).Validate(issued.Token));
    }

    [Fact]
    public void Wrong_issuer_is_rejected()
    {
        var minted = MakeOptions(issuer: "imposter");
        var trusted = MakeOptions(issuer: Issuer);
        var issued = new SessionTokenIssuer(minted).Issue("u");

        Assert.Null(new SessionTokenValidator(trusted).Validate(issued.Token));
    }

    [Fact]
    public void Malformed_token_is_rejected()
    {
        var validator = new SessionTokenValidator(MakeOptions());

        Assert.Null(validator.Validate("not.a.jwt"));
        Assert.Null(validator.Validate(""));
        Assert.Null(validator.Validate("   "));
    }

    [Fact]
    public void Issuer_rejects_short_signing_keys_at_startup()
    {
        var opts = MakeOptions(signingKey: "too-short");
        var issuer = new SessionTokenIssuer(opts);

        Assert.Throws<InvalidOperationException>(() => issuer.Issue("u"));
    }

    [Fact]
    public void Issuer_rejects_empty_sub()
    {
        var issuer = new SessionTokenIssuer(MakeOptions());
        Assert.Throws<ArgumentException>(() => issuer.Issue(""));
        Assert.Throws<ArgumentException>(() => issuer.Issue("   "));
    }

    [Fact]
    public void Apple_token_is_not_accepted_by_session_validator()
    {
        // Cross-validator confusion check: if someone passes an Apple
        // identity token to the session validator (issuer = Apple), the
        // signing key doesn't match → rejected. Guards against a regression
        // where the middleware accidentally accepts Apple JWTs on protected
        // routes.
        var validator = new SessionTokenValidator(MakeOptions());

        // A token with three dot-separated base64-ish segments shaped like a
        // JWT, but signed by no one the validator trusts.
        var fakeApple = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL2FwcGxlaWQuYXBwbGUuY29tIn0.aGVsbG8";
        Assert.Null(validator.Validate(fakeApple));
    }

    private static IOptions<AuthOptions> MakeOptions(
        string? signingKey = null,
        string? audience = null,
        string? issuer = null,
        TimeSpan? lifetime = null)
        => Options.Create(new AuthOptions
        {
            AppleAudience = audience ?? Audience,
            SessionSigningKey = signingKey ?? SigningKey,
            SessionIssuer = issuer ?? Issuer,
            SessionLifetime = lifetime ?? TimeSpan.FromDays(30),
        });

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
