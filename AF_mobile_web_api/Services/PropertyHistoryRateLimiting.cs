using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AF_mobile_web_api.Services;

public static class PropertyHistoryRateLimiting
{
    public const string PolicyName = "property-history";

    public static IServiceCollection AddPropertyHistoryRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("PropertyHistory:PermitLimit", 120);
        var windowSeconds = configuration.GetValue("PropertyHistory:WindowSeconds", 60);
        if (permitLimit <= 0 || windowSeconds <= 0)
            throw new ArgumentException("PropertyHistory:PermitLimit and PropertyHistory:WindowSeconds must be positive.");

        return services.AddRateLimiter(options =>
        {
            // One budget per API instance, independent of forwarded/IP headers. Only
            // the history endpoint opts in; dashboard, health and scraping are unaffected.
            options.AddFixedWindowLimiter(PolicyName, limiter =>
            {
                limiter.PermitLimit = permitLimit;
                limiter.Window = TimeSpan.FromSeconds(windowSeconds);
                limiter.QueueLimit = 0;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                    : windowSeconds;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }
}
