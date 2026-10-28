namespace AF_mobile_web_api.Services
{
    public sealed class OlxScrapeOptions
    {
        public int MaxConcurrentBuckets { get; set; } = 3;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxAttempts { get; set; } = 3;
        public int RetryDelayMilliseconds { get; set; } = 2000;
        public int PageDelayMilliseconds { get; set; } = 250;
    }
}
