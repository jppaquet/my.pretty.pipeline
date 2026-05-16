namespace Notify.Functions.Auth;

// Identity-layer config. The audience claim on Sign-in-with-Apple identity
// tokens is the iOS app's bundle identifier — forks pick their own and set
// `AppleAudience` accordingly. The issuer is constant (Apple) and not
// configurable.
public sealed record AuthOptions
{
    public string AppleAudience { get; init; } = "";
}
