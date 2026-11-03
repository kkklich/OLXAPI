using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services;

// This message is safe to expose in job status; the underlying HTTP response stays in logs.
public sealed class ScrapeSourceException(string source, CityEnum city, Exception inner)
    : Exception($"{source} ({city}): download incomplete.", inner);
