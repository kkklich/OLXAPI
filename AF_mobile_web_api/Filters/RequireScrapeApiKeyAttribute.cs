using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace AF_mobile_web_api.Filters
{
    /// <summary>
    /// Guards scrape-trigger endpoints with an optional API key.
    /// If the "ScrapeApiKey" config value is not set, requests pass through
    /// unchanged so existing deployments keep working. Once configured, the
    /// request must carry a matching "X-Api-Key" header.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireScrapeApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private const string HeaderName = "X-Api-Key";
        private const string ConfigKey = "ScrapeApiKey";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expectedKey = configuration[ConfigKey];

            // Empty/missing key means the gate is intentionally disabled
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return Task.CompletedTask;
            }

            var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();

            // Fixed-time comparison so the key cannot be guessed byte-by-byte via timing
            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);

            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                context.Result = new UnauthorizedResult();
            }

            return Task.CompletedTask;
        }
    }
}
