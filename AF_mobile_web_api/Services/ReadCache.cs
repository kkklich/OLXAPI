using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace AF_mobile_web_api.Services;

// Singleton coordinator for the finite set of dashboard/area cache keys. A scrape
// advances the generation so an in-flight build cannot put old data back in cache.
public sealed class ReadCache(IMemoryCache cache)
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Build = new(1);
        public readonly object Gate = new();
        public long Generation;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<T> GetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory) where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached != null) return cached;
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        await entry.Build.WaitAsync();
        try
        {
            if (cache.TryGetValue(key, out cached) && cached != null) return cached;
            long generation;
            lock (entry.Gate) generation = entry.Generation;
            var value = await factory();
            lock (entry.Gate)
            {
                if (generation == entry.Generation) cache.Set(key, value, ttl);
            }
            return value;
        }
        finally { entry.Build.Release(); }
    }

    public void Remove(string key)
    {
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry.Gate)
        {
            entry.Generation++;
            cache.Remove(key);
        }
    }
}
