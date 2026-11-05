using System.Net;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class OtodomListingsTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? UserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return send(request, token);
        }
    }

    private static (OtodomListingsService Service, Handler Handler) Create(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        }));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        return (new OtodomListingsService(factory.Object), handler);
    }

    private static JObject Offer(string city = "Kraków", int id = 1)
    {
        var offer = JObject.Parse("""
        {"id":1,"title":"Mieszkanie","slug":"mieszkanie-ID1","estate":"FLAT","transaction":"SELL",
         "totalPrice":{"value":600000,"currency":"PLN"},"areaInSquareMeters":50,
         "floorNumber":"THIRD","isPrivateOwner":true,
         "location":{"reverseGeocoding":{"locations":[
            {"locationLevel":"city_or_village","name":"Kraków"},
            {"locationLevel":"district","name":"Centrum"}]}}}
        """);
        offer["id"] = id;
        offer["slug"] = $"mieszkanie-ID{id}";
        offer["location"]!["reverseGeocoding"]!["locations"]![0]!["name"] = city;
        return offer;
    }

    private static string Html(JArray items, int page = 1, int totalPages = 2, int totalItems = 50)
    {
        var root = new JObject
        {
            ["props"] = new JObject { ["pageProps"] = new JObject { ["data"] = new JObject
            {
                ["searchAds"] = new JObject
                {
                    ["items"] = items,
                    ["pagination"] = new JObject
                    {
                        ["itemsPerPage"] = 36, ["totalItems"] = totalItems,
                        ["totalPages"] = totalPages, ["currentPage"] = page
                    }
                }
            } } }
        };
        return $"<html><script id=\"__NEXT_DATA__\" type=\"application/json\" crossorigin=\"anonymous\">{root.ToString(Formatting.None)}</script></html>";
    }

    [Fact]
    public async Task MapsObservedSearchPayloadAndRequestsOnlyOnePage()
    {
        var (service, handler) = Create(Html(new JArray(Offer())));
        var result = await service.GetPageAsync(CityEnum.Krakow, 100000, 700000, 1, 36);
        Assert.Equal("otodom", result.Source);
        Assert.Equal("Krakow", result.City);
        Assert.Equal(50, result.TotalCount);
        Assert.True(result.HasMore);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("RealEstateApp/1.0", handler.UserAgent);
        Assert.Equal("https://www.otodom.pl/pl/wyniki/sprzedaz/mieszkanie/malopolskie/krakow/krakow/krakow?page=1&limit=36&priceMin=100000&priceMax=700000", handler.Url);
        var offer = Assert.Single(result.Data);
        Assert.Equal("1", offer.Id);
        Assert.Equal("https://www.otodom.pl/pl/oferta/mieszkanie-ID1", offer.Url);
        Assert.Equal(600000, offer.Price);
        Assert.Equal(50, offer.Area);
        Assert.Equal(12000, offer.PricePerMeter);
        Assert.Equal(3, offer.Floor);
        Assert.True(offer.Private);
        Assert.Equal(WebName.Otodom, offer.WebName);
        Assert.Equal("Kraków", offer.Location.City);
        Assert.Equal("Centrum", offer.Location.District);
        Assert.Equal(0, offer.Location.Lat);
        Assert.Empty(offer.Market);
        Assert.Empty(offer.BuildingType);
    }

    [Theory]
    [InlineData(CityEnum.Katowice, "slaskie/katowice/katowice/katowice")]
    [InlineData(CityEnum.Chorzow, "slaskie/chorzow/chorzow/chorzow")]
    [InlineData(CityEnum.Tychy, "slaskie/tychy/tychy/tychy")]
    [InlineData(CityEnum.Mikolow, "slaskie/mikolowski/mikolow/mikolow")]
    [InlineData(CityEnum.Myslowice, "slaskie/myslowice/myslowice/myslowice")]
    [InlineData(CityEnum.Sosnowiec, "slaskie/sosnowiec/sosnowiec/sosnowiec")]
    public async Task UsesVerifiedCityPathsAndPagination(CityEnum city, string path)
    {
        var (service, handler) = Create(Html(new JArray(Offer(city.ToDisplayName())), page: 2));
        var result = await service.GetPageAsync(city, 0, 1000000, 2, 36);
        Assert.Equal(city.ToString(), result.City);
        Assert.Contains($"/{path}?page=2&limit=36&priceMin=0&priceMax=1000000", handler.Url);
        Assert.False(result.HasMore);
        Assert.Single(result.Data);
    }

    [Fact]
    public async Task ExcludesOtherCitiesDevelopmentCardsAndOutsidePricePromotionsAndDeduplicates()
    {
        var development = Offer(id: 3);
        development["estate"] = "MULTI";
        var expensive = Offer(id: 4);
        expensive["totalPrice"]!["value"] = 900000;
        var (service, _) = Create(Html(new JArray(Offer(), Offer(), Offer("Katowice", 2), development, expensive)));
        var result = await service.GetPageAsync(CityEnum.Krakow, 100000, 700000, 1, 36);
        Assert.Equal("1", Assert.Single(result.Data).Id);
        Assert.Equal(50, result.TotalCount); // Upstream count is not the number of normalized offers.
    }

    [Theory]
    [InlineData("<html>Access denied</html>")]
    [InlineData("<script id='__NEXT_DATA__'>broken</script>")]
    [InlineData("<script id='__NEXT_DATA__'>{\"props\":{}}</script>")]
    [InlineData("<script id='__NEXT_DATA__'>{\"props\":{\"pageProps\":{\"data\":{\"searchAds\":{\"items\":[],\"pagination\":{}}}}}}</script>")]
    public async Task RejectsMissingOrMalformedListingData(string body)
    {
        var (service, _) = Create(body);
        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36));
    }

    [Fact]
    public async Task EmptySearchIsSuccessfulAndOutOfRangePagesNeverRepeatTheFirstPage()
    {
        var (empty, _) = Create(Html(new JArray(), totalPages: 0, totalItems: 0));
        var result = await empty.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasMore);
        var (redirected, _) = Create(Html(new JArray(Offer()), totalPages: 1, totalItems: 1));
        var laterPage = await redirected.GetPageAsync(CityEnum.Krakow, 0, 1000000, 2, 36);
        Assert.Empty(laterPage.Data);
        Assert.False(laterPage.HasMore);
    }

    [Fact]
    public async Task RejectsPaginationMismatchWithinAvailablePages()
    {
        var (service, _) = Create(Html(new JArray(Offer()), page: 1));
        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 2, 36));
    }

    [Fact]
    public async Task UnknownPriceOnlyAppearsWhenTheRequestedMinimumIsZero()
    {
        var offer = Offer();
        offer["totalPrice"] = null;
        var (service, _) = Create(Html(new JArray(offer)));
        var priced = await service.GetPageAsync(CityEnum.Krakow, 100000, 700000, 1, 36);
        Assert.Empty(priced.Data);
        var all = await service.GetPageAsync(CityEnum.Krakow, 0, 700000, 1, 36);
        Assert.Equal(0, Assert.Single(all.Data).Price);
    }

    [Fact]
    public async Task MalformedIndividualListingIsReportedAsInvalidJson()
    {
        var offer = Offer();
        offer["slug"] = new JObject();
        var (service, _) = Create(Html(new JArray(offer)));
        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 700000, 1, 36));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task PreservesUpstreamHttpFailure(int status)
    {
        var (service, _) = Create("error", (HttpStatusCode)status);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
    }

    [Theory]
    [InlineData(-1, 1000000, 1, 36)]
    [InlineData(1000000, 1, 1, 36)]
    [InlineData(0, 1000000, 0, 36)]
    [InlineData(0, 1000000, 28, 36)]
    [InlineData(0, 1000000, 1, 12)]
    public async Task InvalidFiltersDoNotContactOtodom(int from, int to, int page, int size)
    {
        var (service, handler) = Create("");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetPageAsync(CityEnum.Krakow, from, to, page, size));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CancelsInFlightHttpRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var service = new OtodomListingsService(factory.Object);
        using var cancellation = new CancellationTokenSource();
        var fetch = service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
    }
}
