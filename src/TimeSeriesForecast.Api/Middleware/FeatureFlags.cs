using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TimeSeriesForecast.Api.Data;

namespace TimeSeriesForecast.Api.Middleware;

public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);
    Task InvalidateAsync(string? key = null);
}

public sealed class FeatureFlagsService : IFeatureFlags
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public FeatureFlagsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue<bool>($"ff:{key}", out var enabled)) return enabled;
        enabled = await _db.FeatureFlags.Where(f => f.Key == key).Select(f => f.Enabled).FirstOrDefaultAsync(ct);
        _cache.Set($"ff:{key}", enabled, TimeSpan.FromSeconds(30));
        return enabled;
    }

    public Task InvalidateAsync(string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            // Nothing fancy, let cache expire
            return Task.CompletedTask;
        }
        _cache.Remove($"ff:{key}");
        return Task.CompletedTask;
    }
}
