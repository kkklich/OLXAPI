namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IScrapeJobRunner
    {
        /// <summary>True while a scrape job is executing in the background.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Starts the job in the background unless one is already running.
        /// Returns false (and does nothing) when a job is in progress.
        /// The job receives the service provider of its own DI scope, so it can resolve
        /// any service - never capture the request's services, they die with the request.
        /// </summary>
        bool TryStart(string jobName, Func<IServiceProvider, Task> job);

        /// <summary>
        /// Shorthand for the common case: a job that runs on <see cref="IRealEstateServices"/>
        /// resolved from the job's own scope.
        /// </summary>
        bool TryStart(string jobName, Func<IRealEstateServices, Task> job);
    }
}
