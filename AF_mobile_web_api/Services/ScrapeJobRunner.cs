using AF_mobile_web_api.Services.Interfaces;

namespace AF_mobile_web_api.Services
{
    // A full scrape takes minutes - far longer than reverse-proxy request timeouts -
    // so the controller returns 202 and the job runs here, detached from the request.
    // Singleton: the _running flag also serializes scrapes across requests, because two
    // concurrent scrapes would write two overlapping "latest" batches to the database.
    public class ScrapeJobRunner : IScrapeJobRunner
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScrapeJobRunner> _logger;
        private int _running; // 0 = idle, 1 = busy (Interlocked)

        public ScrapeJobRunner(IServiceScopeFactory scopeFactory, ILogger<ScrapeJobRunner> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public bool TryStart(string jobName, Func<IRealEstateServices, Task> job) =>
            TryStart(jobName, (IServiceProvider services) => job(services.GetRequiredService<IRealEstateServices>()));

        // Any job, not just a full scrape: the single-portal debug scrapes run here too, so
        // they share the one-at-a-time flag and cannot overlap a real run hammering the same
        // portals.
        public bool TryStart(string jobName, Func<IServiceProvider, Task> job)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return false;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // The request's scoped services (DbContext included) are disposed when
                    // the response is sent, so the job needs its own scope.
                    using var scope = _scopeFactory.CreateScope();

                    _logger.LogInformation("Scrape job {Job} started", jobName);
                    await job(scope.ServiceProvider);
                    _logger.LogInformation("Scrape job {Job} finished", jobName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scrape job {Job} failed", jobName);
                }
                finally
                {
                    Volatile.Write(ref _running, 0);
                }
            });

            return true;
        }
    }
}
