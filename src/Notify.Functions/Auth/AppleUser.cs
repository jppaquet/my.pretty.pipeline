namespace Notify.Functions.Auth;

// Validated principal extracted from a Sign-in-with-Apple identity token.
// `Sub` is Apple's stable, per-app, per-user identifier — opaque, unguessable,
// and the only durable handle we get; it becomes the inbox/device partition key
// in PR-C. We do not capture `email`/`email_verified` here because the Apple
// docs note those claims are only present on first sign-in (not on subsequent
// silent refreshes) and we have no need for them.
public sealed record AppleUser(string Sub);
