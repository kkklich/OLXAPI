using System.Net;

namespace AF_mobile_web_api.Services
{
    public sealed class UpstreamHttpException : HttpRequestException
    {
        public TimeSpan? RetryAfter { get; }

        public UpstreamHttpException(string message, HttpStatusCode status, TimeSpan? retryAfter)
            : base(message, null, status) => RetryAfter = retryAfter;
    }
}
