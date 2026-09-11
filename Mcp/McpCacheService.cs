using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MB.ComTools.Apps.Setup.Mcp;

/// <summary>
/// Thin IMemoryCache wrapper for MCP resources and tools.
/// All keys are prefixed with "mcp:" to avoid collisions.
/// </summary>
public class McpCacheService
{
    private readonly IMemoryCache _cache;
    private readonly McpOptions _options;

    // CancellationTokenSources for entity-type invalidation
    private CancellationTokenSource _coursesCts = new();
    private CancellationTokenSource _profilesCts = new();
    private CancellationTokenSource _tagsCts = new();
    private CancellationTokenSource _divisionsCts = new();
    private CancellationTokenSource _siteConfigCts = new();

    public McpCacheService(IMemoryCache cache, McpOptions options)
    {
        _cache = cache;
        _options = options;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        string tokenName,
        Func<Task<T>> factory,
        Func<T, bool>? isNegative = null)
    {
        // Skip cache when TTL is 0 (e.g., test environment)
        if (_options.CacheTtlMinutes <= 0)
            return await factory();

        var fullKey = $"mcp:{key}";
        var cts = GetCts(tokenName);

        return await _cache.GetOrCreateAsync(fullKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheTtlMinutes);
            entry.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token));
            entry.Size = 1; // Required when IMemoryCache is configured with SizeLimit
            var value = await factory();
            if (isNegative?.Invoke(value) == true && _options.NegativeCacheTtlMinutes > 0)
            {
                entry.AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromMinutes(_options.NegativeCacheTtlMinutes);
            }

            return value;
        });
    }

    /// <summary>
    /// Invalidates all cache entries for the given entity type.
    /// Call after any write to LearnCourse, LearnProfile, or Tag entities.
    /// </summary>
    public void Invalidate(string tokenName)
    {
        var old = GetCts(tokenName);
        var replacement = new CancellationTokenSource();
        switch (tokenName)
        {
            case "courses": _coursesCts = replacement; break;
            case "profiles": _profilesCts = replacement; break;
            case "tags": _tagsCts = replacement; break;
            case "divisions": _divisionsCts = replacement; break;
            case "site-config": _siteConfigCts = replacement; break;
        }
        old.Cancel();
        old.Dispose();
    }

    /// <summary>Computes a stable short hash for search parameter objects.</summary>
    public static string HashParams(object parameters)
    {
        var json = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = false });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..16]; // first 16 chars is sufficient
    }

    private CancellationTokenSource GetCts(string tokenName) => tokenName switch
    {
        "courses" => _coursesCts,
        "profiles" => _profilesCts,
        "tags" => _tagsCts,
        "divisions" => _divisionsCts,
        "site-config" => _siteConfigCts,
        _ => _coursesCts
    };
}
