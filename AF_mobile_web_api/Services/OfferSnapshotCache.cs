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
        private Task<IReadOnlyList<OfferSnapshot>>? _load;
        private DateTime _loadedAt;

        public OfferSnapshotCache(IServiceScopeFactory scopeFactory, ILogger<OfferSnapshotCache> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<IReadOnlyList<OfferSnapshot>> GetAsync()
        {
            lock (_gate)
            {
                if (_load is null || IsUnusable(_load))
                {
                    _load = LoadAsync();
                }

                return _load;
            }
        }

        public void Invalidate()
        {
            lock (_gate)
            {
                _load = null;
            }
        }

        // A failed load must not be cached - the next request should retry rather than
        // replay the exception for the next two hours.
        private bool IsUnusable(Task<IReadOnlyList<OfferSnapshot>> load) =>
            load.IsFaulted
            || load.IsCanceled
            || (load.IsCompletedSuccessfully && DateTime.UtcNow - _loadedAt > Ttl);

        private async Task<IReadOnlyList<OfferSnapshot>> LoadAsync()
        {
            // Yield first: the load runs outside the lock held by the caller that started it.
            await Task.Yield();

            var stopwatch = Stopwatch.StartNew();

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPropertyDataRepository>();

            var builder = new OfferSnapshotBuilder();
            var offers = new List<OfferSnapshot>();

            // Streamed rather than materialised as a list first: the rows and the snapshot
            // would otherwise both be in memory at the peak, for ~95k offers.
            await foreach (var row in repository.StreamLatestOffersAsync())
            {
                offers.Add(builder.Build(row));
            }

            _loadedAt = DateTime.UtcNow;
            _logger.LogInformation("Loaded offers snapshot: {Count} offers in {Elapsed} ms", offers.Count, stopwatch.ElapsedMilliseconds);

            return offers;
        }
    }
}
