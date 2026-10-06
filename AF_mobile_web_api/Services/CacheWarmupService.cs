using AF_mobile_web_api.Domain;
using AF_mobile_web_api.Services.Interfaces;

namespace AF_mobile_web_api.Services
{
    // Builds the read caches once at startup instead of letting the first visitor build them.
    //
    // All are expensive and none depends on the request: the offers snapshot is a
    // full-table scan (~7s), and each dashboard's statistics and price drops are scans of
    // its cities' rows. The API sleeps when idle on Render, so without this every visit after an
    // idle period pays for them - and the offers list, which nobody opens first, pays most.
    //
    // Only what the dashboard offers is warmed (Krakow, Silesia), not each Silesian city on
    // its own as well: nothing requests those, and each would hold its own copy of the rows.
    //
    // Deliberately tolerant: a warm-up failure (the database is unreachable, say) must never
    // stop the API from starting. The request path builds the same caches on demand anyway.
    public sealed class CacheWarmupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOfferSnapshotCache _offers;
        private readonly ILogger<CacheWarmupService> _logger;

        public CacheWarmupService(
            IServiceScopeFactory scopeFactory,
            IOfferSnapshotCache offers,
            ILogger<CacheWarmupService> logger)
        {
            _scopeFactory = scopeFactory;
            _offers = offers;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the host finish starting before competing with it for the connection pool.
            await Task.Yield();

            await WarmAsync("offers snapshot", () => _offers.GetAsync(), stoppingToken);

            foreach (var area in CityArea.DashboardAreas)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;

                await WarmAsync($"dashboard {area.Name}", async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var statistics = scope.ServiceProvider.GetRequiredService<IStatisticServices>();
                    await statistics.GetFullDashboardDataAsync(area.Name);
                }, stoppingToken);


                // Requested alongside the dashboard on every visit, and ~1.8s to build cold.
                await WarmAsync($"price drops {area.Name}", async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var statistics = scope.ServiceProvider.GetRequiredService<IStatisticServices>();
                    await statistics.GetPriceDrops(area.Name, 1);
                }, stoppingToken);
            }
        }

        private async Task WarmAsync(string what, Func<Task> warm, CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                await warm();
                _logger.LogInformation("Warmed up {What}", what);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Warm-up of {What} failed; it will be built on demand", what);
            }
        }
    }
}
