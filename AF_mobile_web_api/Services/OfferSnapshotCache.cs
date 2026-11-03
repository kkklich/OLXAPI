using System.Diagnostics;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;

namespace AF_mobile_web_api.Services
{
    // Holds the deduplicated offers set - one entry per Url, its newest snapshot - for the
    // paged list to filter in memory.
    //
    // Rebuilding it costs one ~7s scan of the whole table, which is why it is a singleton
    // and not per request: the table only changes when a scrape lands, so the scan is paid
    // once (at startup, by the warm-up) instead of on every page, sort and filter.
    //
    // Loading runs in its own DI scope. The DbContext is scoped, and a request-scoped one
    // would be disposed the moment its request ends - which, with several requests sharing
    // one load, is not the request that started it.
    public sealed class OfferSnapshotCache : IOfferSnapshotCache
    {
        // Matches the dashboard's cache: a stale list is only ever as old as the scrape
        // that a restart or a missed eviction hid from this instance.
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(120);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OfferSnapshotCache> _logger;

        private readonly object _gate = new();
        private Task<LoadedSnapshot>? _load;
        private LoadedSnapshot? _current;
        private long _generation;
        private DateTime _validUntil;
        private DateTime _retryAt;

        public OfferSnapshotCache(IServiceScopeFactory scopeFactory, ILogger<OfferSnapshotCache> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<OfferSnapshot>> GetAsync()
        {
            Task<LoadedSnapshot> load;

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if ((_current is null || now >= _validUntil)
                    && (_load is null || _load.IsCompleted) && now >= _retryAt)
                {
                    var generation = _generation;
                    _load = RefreshAsync(generation);
                    // If a scrape invalidates an already-running build, queue a fresh
                    // build once it completes instead of waiting for the next visitor.
                    _ = _load.ContinueWith(completed =>
                    {
                        lock (_gate)
                        {
                            if (!ReferenceEquals(_load, completed) || _generation == generation) return;
                        }
                        _ = RefreshInBackgroundAsync();
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }

                // Keep the last complete snapshot available while its replacement loads.
                if (_current != null) return _current.Offers;
                load = _load!;
            }

            // Awaited outside the lock: every caller shares the one load, only this unwrap
            // is per caller.
            return (await load).Offers;
        }

        public void Invalidate()
        {
            lock (_gate)
            {
                _generation++;
                _validUntil = DateTime.MinValue;
                _retryAt = DateTime.MinValue;
            }
            _ = RefreshInBackgroundAsync();
        }

        private async Task RefreshInBackgroundAsync()
        {
            try { await GetAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Offers snapshot refresh failed; the next request can retry"); }
        }

        private async Task<LoadedSnapshot> RefreshAsync(long generation)
        {
            try
            {
                var loaded = await LoadAsync();
                lock (_gate)
                {
                    if (generation == _generation)
                    {
                        _current = loaded;
                        _validUntil = loaded.LoadedAt + Ttl;
                    }
                }
                return loaded;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (_current is null) throw;
                    _retryAt = DateTime.UtcNow.AddSeconds(30);
                    _logger.LogWarning(ex, "Serving the previous offers snapshot after a refresh failure");
                    return _current;
                }
            }
        }

        private async Task<LoadedSnapshot> LoadAsync()
        {
            // Yield first: the load runs outside the lock held by the caller that started it.
            await Task.Yield();

            var stopwatch = Stopwatch.StartNew();

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPropertyDataRepository>();

            var builder = new OfferSnapshotBuilder();
            var offers = new List<OfferSnapshot>();

            // The load joins on (Url, AddedRecordTime) and relies on that pair being unique.
            // Should two rows ever share an offer's oldest pair, the join to the first price
            // repeats its newest row once per match: two entries with one Id tie on every
            // sort key, the Id tiebreaker included, so they are free to straddle a page
            // boundary and show on both pages. The first one streamed wins. (Two rows sharing
            // the newest pair are two Ids - listed twice, but pages still cannot overlap.)
            var seen = new HashSet<Guid>();
            var duplicates = 0;

            // Streamed rather than materialised as a list first: the rows and the snapshot
            // would otherwise both be in memory at the peak, for ~95k offers.
            await foreach (var row in repository.StreamLatestOffersAsync())
            {
                if (!seen.Add(row.Id))
                {
                    duplicates++;
                    continue;
                }

                offers.Add(builder.Build(row));
            }

            if (duplicates > 0)
            {
                _logger.LogWarning("Offers snapshot skipped {Duplicates} duplicate rows: (Url, AddedRecordTime) is not unique for some offers", duplicates);
            }

            _logger.LogInformation("Loaded offers snapshot: {Count} offers in {Elapsed} ms", offers.Count, stopwatch.ElapsedMilliseconds);

            return new LoadedSnapshot(offers, DateTime.UtcNow);
        }

        // One load's result together with when it finished, so a load's age can never be
        // confused with another's.
        private sealed record LoadedSnapshot(IReadOnlyList<OfferSnapshot> Offers, DateTime LoadedAt);
    }
}
