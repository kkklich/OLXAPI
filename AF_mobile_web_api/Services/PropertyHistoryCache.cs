using AF_mobile_web_api.DTO;
using Microsoft.Extensions.Caching.Memory;

namespace AF_mobile_web_api.Services;

// URLs are supplied by visitors, so neither cached values nor per-key locks may grow
// without a bound. Keep histories separately from the finite dashboard cache keys.
public sealed class PropertyHistoryCache : IDisposable
{
    private sealed record Value(PropertyHistoryDTO? History);

    private readonly MemoryCache _cache;
    private readonly SemaphoreSlim[] _builds = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly object _gate = new();
    private long _generation;

    // Size is a conservative estimate of retained UTF-16 strings and object overhead,
    // not a process-memory limit. Oversize histories are returned without caching.
    public PropertyHistoryCache(long sizeLimit = 16 * 1024 * 1024)
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit });
    }

    public async Task<PropertyHistoryDTO?> GetAsync(string city, string url,
        Func<CancellationToken, Task<PropertyHistoryDTO?>> factory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (city, url);
        if (_cache.TryGetValue(key, out Value? cached)) return cached!.History;

        var build = _builds[(key.GetHashCode() & int.MaxValue) % _builds.Length];
        await build.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(key, out cached)) return cached!.History;
            long generation;
            lock (_gate) generation = _generation;
            var history = await factory(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                // A scrape completed while this query was running: its result may still
                // satisfy that visitor, but must never repopulate the new generation.
                if (generation == _generation)
                    _cache.Set(key, new Value(history), new MemoryCacheEntryOptions
                    {
                        Size = EstimateSize(city, url, history)
                    });
            }
            return history;
        }
        finally { build.Release(); }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _cache.Clear();
        }
    }

    private static long EstimateSize(string city, string url, PropertyHistoryDTO? history)
    {
        static long Text(string? value) => value is null ? 0 : 32L + 2L * value.Length;
        var size = 512L + Text(city) + Text(url);
        if (history is null) return size;
        size += 512L + Text(history.Url) + Text(history.Title) + Text(history.City) + Text(history.District)
            + Text(history.Market) + Text(history.BuildingType) + Text(history.OffertId) + Text(history.Description);
        foreach (var entry in history.Entries) size += 128L + Text(entry.Url);
        return size;
    }

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var build in _builds) build.Dispose();
    }
}
