using System.Net;
using System.Net.Http.Headers;
using AF_mobile_web_api.Services;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class HttpClientServicesTests
{
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("busy") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task PreservesStatusAndRetryAfterForDownloader()
    {
        using var client = new HttpClient(new Handler());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var service = new HTTPClientServices(factory.Object);
        var error = await Assert.ThrowsAsync<UpstreamHttpException>(() => service.GetRaw("https://www.olx.pl/test"));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
    }
}
