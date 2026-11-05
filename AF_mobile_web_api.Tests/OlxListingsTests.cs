using System.Net;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace AF_mobile_web_api.Tests;

public sealed class OlxListingsTests
{
    private sealed class ResponseHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (OlxListingsService Service, ResponseHandler Handler) Create(
        string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new ResponseHandler(body, status);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return (new OlxListingsService(factory.Object), handler);
    }

    [Theory]
    [InlineData(CityEnum.Krakow, 4, 8959)]
    [InlineData(CityEnum.Katowice, 6, 7691)]
    [InlineData(CityEnum.Chorzow, 6, 22611)]
    [InlineData(CityEnum.Tychy, 6, 17705)]
    [InlineData(CityEnum.Mikolow, 6, 11631)]
    [InlineData(CityEnum.Myslowice, 6, 50917)]
    [InlineData(CityEnum.Sosnowiec, 6, 16027)]
    public async Task RequestsOnePageForTheExactCityWithPriceAndPaginationFilters(CityEnum city, int regionId, int cityId)
    {
        var (service, handler) = Create("""{"data":[],"metadata":{"total_elements":1000,"visible_total_count":2345}}""");
        var page = await service.GetPageAsync(city, 100000, 700000, 2, 36);

        var url = Assert.Single(handler.Requests);
        Assert.Contains("offset=36&limit=36&category_id=14", url);
        Assert.Contains("filter_float_price%3Afrom=100000&filter_float_price%3Ato=700000", url);
        Assert.Contains($"region_id={regionId}&city_id={cityId}", url);
        Assert.Equal("olx", page.Source);
        Assert.Equal(city.ToString(), page.City);
        Assert.Equal(2345, page.TotalCount);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task MapsPartnerOffersToCanonicalOtodomUrlsAndKeepsNativeOlxOffers()
    {
        var (service, handler) = Create("""
            {"data":[
              {"id":101,"url":"https://www.olx.pl/d/oferta/a.html","title":"Mieszkanie",
               "partner":{"code":"otodom_pl"},"external_url":"https://otodom.pl/pl/oferta/home-ID123/?campaign=olx#photo",
               "params":[{"key":"m","value":{"key":"25.5"}},{"key":"price","value":{"label":"420 000 zł"}}],
               "location":{"city":{"id":8959,"name":"Kraków"},"district":{"name":"Prądnik Biały"}},"map":{"lat":50.09,"lon":19.92}},
              {"id":102,"url":"https://www.olx.pl/d/oferta/b.html","title":"Mieszkanie",
               "partner":{"code":"otodom_pl"},"external_url":"https://www.otodom.pl/pl/oferta/home-ID123",
               "location":{"city":{"id":8959,"name":"Kraków"}}},
              {"id":103,"url":"https://www.olx.pl/d/oferta/c.html","title":"Native OLX","location":{"city":{"id":8959,"name":"Kraków"}}}
            ],"metadata":{"visible_total_count":3}}
            """);
        var page = await service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36);

        Assert.Single(handler.Requests);
        Assert.Equal(2, page.Data.Count);
        var partner = page.Data[0];
        Assert.Equal("https://www.otodom.pl/pl/oferta/home-ID123", partner.Url);
        Assert.Equal(WebName.Otodom, partner.WebName);
        Assert.Equal(420000, partner.Price);
        Assert.Equal(25.5, partner.Area);
        Assert.Equal(420000 / 25.5, partner.PricePerMeter, 2);
        Assert.Equal("Kraków", partner.Location.City);
        Assert.Equal(50.09, partner.Location.Lat);
        Assert.Equal(WebName.OLX, page.Data[1].WebName);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ExcludesNearbyCitiesMissingIdentityAndTbsAndRejectsUntrustedExternalHost()
    {
        var (service, _) = Create("""
            {"data":[
              {"id":1,"url":"https://www.olx.pl/d/oferta/one","title":"Nearby","location":{"city":{"id":7691,"name":"Katowice"}}},
              {"id":2,"url":"https://www.olx.pl/d/oferta/two","title":"TBS","location":{"city":{"id":11631,"name":"Mikołów"}}},
              {"id":3,"title":"No URL","location":{"city":{"id":11631,"name":"Mikołów"}}},
              {"id":4,"url":"https://www.olx.pl/d/oferta/four","title":"Mieszkanie","location":{"city":{"id":11631,"name":"Mikołów"}},
               "partner":{"code":"otodom_pl"},"external_url":"https://www.otodom.pl.evil.test/pl/oferta/trick"}
            ]}
            """);
        var page = await service.GetPageAsync(CityEnum.Mikolow, 0, 1000000, 1, 36);
        var offer = Assert.Single(page.Data);
        Assert.Equal("4", offer.Id);
        Assert.Equal(WebName.OLX, offer.WebName);
        Assert.Equal("https://www.olx.pl/d/oferta/four", offer.Url);
        Assert.Null(page.TotalCount);
    }

    [Theory]
    [InlineData(-1, 100, 1, 36)]
    [InlineData(100, 99, 1, 36)]
    [InlineData(0, 100, 0, 36)]
    [InlineData(0, 100, 1, 0)]
    [InlineData(0, 100, 1, 41)]
    [InlineData(0, 100, 28, 36)]
    [InlineData(0, 100, int.MaxValue, 36)]
    public async Task InvalidFiltersFailBeforeHttp(int priceFrom, int priceTo, int page, int pageSize)
    {
        var (service, handler) = Create("{}");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetPageAsync(CityEnum.Krakow, priceFrom, priceTo, page, pageSize));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OutOfRangePromotionsAreExcludedWithoutChangingUpstreamTotal()
    {
        var (service, _) = Create("""
            {"data":[
              {"id":1,"url":"https://www.olx.pl/d/oferta/one","title":"In range","params":[{"key":"price","value":{"label":"420 000 zł"}}],"location":{"city":{"id":8959,"name":"Kraków"}}},
              {"id":2,"url":"https://www.olx.pl/d/oferta/two","title":"Too expensive","params":[{"key":"price","value":{"label":"720 000 zł"}}],"location":{"city":{"id":8959,"name":"Kraków"}}},
              {"id":3,"url":"https://www.olx.pl/d/oferta/three","title":"Unknown price","location":{"city":{"id":8959,"name":"Kraków"}}}
            ],"metadata":{"visible_total_count":3}}
            """);
        var page = await service.GetPageAsync(CityEnum.Krakow, 100000, 500000, 1, 36);
        Assert.Equal("1", Assert.Single(page.Data).Id);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task UnknownCityDoesNotRequestAnUnboundedRegionSearch()
    {
        var (service, handler) = Create("{}");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPageAsync((CityEnum)999, 0, 1000000, 1, 36));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"data\":null}")] [InlineData("<html>denied</html>")]
    public async Task InvalidPayloadFailsInsteadOfReturningAnEmptySuccess(string body)
    {
        var (service, _) = Create(body);
        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36));
    }

    [Fact]
    public async Task UpstreamHttpFailurePreservesStatusCode()
    {
        var (service, _) = Create("denied", HttpStatusCode.Forbidden);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task RequestCancellationIsPropagated()
    {
        var (service, handler) = Create("{\"data\":[]}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 1, 36, cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RemainingUpstreamDataIsReportedAtThePaginationBoundary()
    {
        var (service, _) = Create("""{"data":[],"metadata":{"total_elements":1000,"visible_total_count":2345}}""");
        var page = await service.GetPageAsync(CityEnum.Krakow, 0, 1000000, 27, 36);
        Assert.True(page.HasMore);
        Assert.Equal(2345, page.TotalCount);
    }
}
