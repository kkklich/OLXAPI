namespace AF_mobile_web_api.Services;

public sealed record ScrapeJobStatus(bool IsRunning = false, bool? LastRunSucceeded = null,
    DateTimeOffset? LastCompletedAt = null, string? LastError = null);
