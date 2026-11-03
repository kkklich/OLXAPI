using AF_mobile_web_api.Services.Interfaces;

namespace AF_mobile_web_api.Services
{
    // A full scrape takes minutes - far longer than reverse-proxy request timeouts -
    // so the controller returns 202 and the job runs here, detached from the request.
    // Singleton: the status lock also serializes scrapes across requests, because two
    // concurrent scrapes would write two overlapping "latest" batches to the database.
    public class ScrapeJobRunner : IScrapeJobRunner
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScrapeJobRunner> _logger;
        private readonly object _gate = new();
        private ScrapeJobStatus _status = new();

        public ScrapeJobRunner(IServiceScopeFactory scopeFactory, ILogger<ScrapeJobRunner> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public ScrapeJobStatus Status { get { lock (_gate) return _status; } }
        public bool IsRunning => Status.IsRunning;

        public bool TryStart(string jobName, Func<IRealEstateServices, Task> job) =>
            TryStart(jobName, (IServiceProvider services) => job(services.GetRequiredService<IRealEstateServices>()));

        // Any job, not just a full scrape: the single-portal debug scrapes run here too, so
        // they share the one-at-a-time flag and cannot overlap a real run hammering the same
        // portals.
        public bool TryStart(string jobName, Func<IServiceProvider, Task> job)
        {
            lock (_gate)
            {
                if (_status.IsRunning) return false;
                _status = new ScrapeJobStatus(IsRunning: true);
            }

            _ = Task.Run(async () =>
            {
                string? failure = null;
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
                    var errors = ex is AggregateException aggregate
                        ? aggregate.Flatten().InnerExceptions.AsEnumerable() : new[] { ex };
                    var sources = errors.OfType<ScrapeSourceException>().Select(error => error.Message).Distinct().ToList();
                    failure = sources.Count > 0 ? string.Join(" ", sources) : "Scrape failed; check server logs.";
                }
                finally
                {
                    lock (_gate) _status = new ScrapeJobStatus(false, failure == null, DateTimeOffset.UtcNow, failure);
                }
            });

            return true;
        }
    }
}
