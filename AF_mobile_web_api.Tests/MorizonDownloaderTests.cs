using System.Collections.Concurrent;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class MorizonDownloaderTests
{
    private static JObject Page(int total, params int[] ids) => JObject.FromObject(new
    {
        data = new { searchResult = new { properties = new { totalCount = total,
            nodes = ids.Select(id => new { id, url = $"/oferta/{id}", title = "Mieszkanie", area = "50",
                price = new { amount = "500000" }, priceM2 = new { amount = "10000" } }) } } }
    });

    private static MorizonApiService Service(Func<string, JObject> respond)
    {
        var http = new Mock<IHTTPClientServices>();
        http.Setup(x => x.PostAsync<object, JObject>(It.IsAny<string>(), It.IsAny<object>(), false, 30.0))
            .Returns((string endpoint, object body, bool token, double? timeout) =>
                Task.FromResult(respond(JObject.FromObject(body)["variables"]!["url"]!.Value<string>()!)));
        return new MorizonApiService(http.Object, NullLogger<MorizonApiService>.Instance);
    }

    [Fact]
    public async Task SparseCityUsesSixSearchesInsteadOf59400Requests()
    {
        var calls = 0;
        var service = Service(_ => Page(1, Interlocked.Increment(ref calls)));
        var result = await service.GetPropertyListingDataAsync(CityEnum.Krakow);
        Assert.Equal(6, calls);
        Assert.Equal(6, result.Data.Count);
        Assert.All(result.Data, offer => Assert.Contains(offer.Market, new[] { "Pierwotny", "Wtórny" }));
    }

    [Fact]
    public async Task OnlyRequestsThePagesReportedByTheFirstSearch()
    {
        var urls = new List<string>();
        var service = Service(url => { urls.Add(url); return url.Contains("page=2") ? Page(3, 3) : Page(3, 1, 2); });
        var result = await service.GetPropertyListingDataAsync(searchUrl: "/mieszkania/krakow/?x=1");
        Assert.Equal(2, urls.Count);
        Assert.Equal(3, result.Data.Count);
    }

    [Fact]
    public async Task DenseSearchSplitsBeforeRequestingTruncatedPages()
    {
        var urls = new ConcurrentBag<string>();
        var service = Service(url =>
        {
            urls.Add(url);
            var decoded = Uri.UnescapeDataString(url);
            return decoded.Contains("price_from]=100000&ps[price_to]=10000000")
                ? Page(12, 1, 2) : Page(2, 1, 2);
        });
        var result = await service.GetPropertyListingDataAsync();
        Assert.Equal(18, urls.Count);
        Assert.DoesNotContain(urls, url => url.Contains("page="));
        Assert.Equal(2, result.Data.Count);
    }

    [Fact]
    public async Task ReportsARepeatedPageAndRetainsDownloadedOffers()
    {
        var service = Service(_ => Page(3, 1, 2));
        var error = await Assert.ThrowsAsync<PartialScrapeException>(() =>
            service.GetPropertyListingDataAsync(searchUrl: "/mieszkania/krakow/?x=1"));
        Assert.Equal(2, error.Offers.Count);
    }

    [Fact]
    public async Task DoesNotMistakeGraphqlErrorsForAnEmptyCity()
    {
        var service = Service(_ => JObject.FromObject(new { errors = new[] { new { message = "unavailable" } } }));
        await Assert.ThrowsAsync<PartialScrapeException>(() => service.GetPropertyListingDataAsync());
    }
}
