using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Auth;

// Fetches and caches Apple's JSON Web Key Set used to verify Sign-in-with-Apple
// identity tokens. The endpoint is public and constant:
//     https://appleid.apple.com/auth/keys
// Apple rotates keys infrequently (months/years) and announces new keys via
// the JWKS itself, so a 24h cache is plenty. Cache hits are pure in-memory;
// cache misses make one HTTPS request.
//
// Factored behind IAppleJwksProvider so `AppleJwtValidator` is pure and
// unit-testable with a synthetic JWKS.
public interface IAppleJwksProvider
{
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct);
}

public sealed class AppleJwksProvider : IAppleJwksProvider
{
    public const string AppleJwksUrl = "https://appleid.apple.com/auth/keys";
    private const string CacheKey = "apple-jwks";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public AppleJwksProvider(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyCollection<SecurityKey>? cached) && cached is not null)
            return cached;

        var json = await _http.GetStringAsync(AppleJwksUrl, ct);
        var jwks = new JsonWebKeySet(json);
        var keys = jwks.GetSigningKeys().ToArray();

        _cache.Set(CacheKey, (IReadOnlyCollection<SecurityKey>)keys, CacheTtl);
        return keys;
    }
}
