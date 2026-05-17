using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Notify.Functions.Admin;

// Fetches and caches Entra's JSON Web Key Set used to verify the admin app's
// access tokens. Endpoint is tenant-scoped (single-tenant app registration):
//     https://login.microsoftonline.com/{tenant_id}/discovery/v2.0/keys
// Microsoft rotates keys quarterly and announces new keys via the JWKS, so
// a 24h cache is plenty. Mirrors [[AppleJwksProvider]] structurally — same
// caching pattern, different endpoint shape.
public interface IEntraJwksProvider
{
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct);
}

public sealed class EntraJwksProvider : IEntraJwksProvider
{
    public const string EndpointFormat = "https://login.microsoftonline.com/{0}/discovery/v2.0/keys";
    private const string CacheKey = "entra-jwks";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AdminOptions> _opts;

    public EntraJwksProvider(HttpClient http, IMemoryCache cache, IOptions<AdminOptions> opts)
    {
        _http = http;
        _cache = cache;
        _opts = opts;
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyCollection<SecurityKey>? cached) && cached is not null)
            return cached;

        var tenantId = _opts.Value.EntraTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            return Array.Empty<SecurityKey>();

        var url = string.Format(EndpointFormat, tenantId);
        var json = await _http.GetStringAsync(url, ct);
        var jwks = new JsonWebKeySet(json);
        var keys = jwks.GetSigningKeys().ToArray();

        _cache.Set(CacheKey, (IReadOnlyCollection<SecurityKey>)keys, CacheTtl);
        return keys;
    }
}
