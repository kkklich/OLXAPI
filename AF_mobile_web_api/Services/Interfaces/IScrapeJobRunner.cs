namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IScrapeJobRunner
    {
        /// <summary>True while a scrape job is executing in the background.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Starts the job in the background unless one is already running.
        /// Returns false (and does nothing) when a job is in progress.
        /// </summary>
        bool TryStart(string jobName, Func<IRealEstateServices, Task> job);
    }
}
