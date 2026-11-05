using System.Net;
using System.Reflection;
using AF_mobile_web_api.Controllers;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Filters;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class MarketplaceListingsTests
{
    private sealed class Harness
    {
        public Mock<IOlxListingsService> Olx { get; } = new(MockBehavior.Strict);
        public Mock<IOtodomListingsService> Otodom { get; } = new(MockBehavior.Strict);
        public MarketplaceListingsService Service { get; }
        public MarketplaceListingsController Controller { get; }

        public Harness()
        {
            Service = new MarketplaceListingsService(Olx.Object, Otodom.Object,
                NullLogger<MarketplaceListingsService>.Instance);
            Controller = new MarketplaceListingsController(Service);
        }

        public void EmptyPages()
        {
            Olx.Setup(x => x.GetPageAsync(It.IsAny<CityEnum>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CityEnum city, int from, int to, int page, int size, CancellationToken token) =>
                    new MarketplaceCityPage { City = city.ToString(), Source = "olx" });
            Otodom.Setup(x => x.GetPageAsync(It.IsAny<CityEnum>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CityEnum city, int from, int to, int page, int size, CancellationToken token) =>
                    new MarketplaceCityPage { City = city.ToString(), Source = "otodom" });
        }
    }

    [Fact]
    public async Task SilesiaFetchesExactlySixCitiesFromBothSourcesWithRequestedFilters()
    {
        var h = new Harness();
        h.EmptyPages();
        var result = await h.Service.GetAsync("silesia", "ALL", 200000, 900000, 2);

        Assert.Equal("Silesia", result.City);
        Assert.Equal("all", result.Source);
        Assert.False(result.SavedToDatabase);
        Assert.False(result.IsPartial);
        Assert.Equal(12, result.Pages.Count);
        var cities = new[] { CityEnum.Katowice, CityEnum.Chorzow, CityEnum.Tychy,
            CityEnum.Mikolow, CityEnum.Myslowice, CityEnum.Sosnowiec };
        foreach (var city in cities)
        {
            h.Olx.Verify(x => x.GetPageAsync(city, 200000, 900000, 2, 36, It.IsAny<CancellationToken>()), Times.Once);
            h.Otodom.Verify(x => x.GetPageAsync(city, 200000, 900000, 2, 36, It.IsAny<CancellationToken>()), Times.Once);
        }
        h.Olx.VerifyNoOtherCalls();
        h.Otodom.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Krakow", CityEnum.Krakow)]
    [InlineData(" kraków ", CityEnum.Krakow)]
    [InlineData("CHORZÓW", CityEnum.Chorzow)]
    [InlineData("Mikołów", CityEnum.Mikolow)]
    [InlineData("Mysłowice", CityEnum.Myslowice)]
    public async Task AcceptsCanonicalAndPolishNamesWithSingleSource(string city, CityEnum expectedCity)
    {
        var h = new Harness();
        h.EmptyPages();
        var result = await h.Service.GetAsync(city, " Otodom ");
        Assert.Equal(expectedCity.ToString(), result.City);
        Assert.Equal("otodom", Assert.Single(result.Pages).Source);
        h.Otodom.Verify(x => x.GetPageAsync(expectedCity, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()), Times.Once);
        h.Olx.VerifyNoOtherCalls();
        h.Otodom.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("unknown", "all", 0, 1000000, 1)]
    [InlineData("1", "all", 0, 1000000, 1)]
    [InlineData("", "all", 0, 1000000, 1)]
    [InlineData("Krakow", "unknown", 0, 1000000, 1)]
    [InlineData("Krakow", "", 0, 1000000, 1)]
    [InlineData("Krakow", "olx", -1, 1000000, 1)]
    [InlineData("Krakow", "olx", 500000, 499999, 1)]
    [InlineData("Krakow", "all", 0, 1000000, 0)]
    [InlineData("Krakow", "all", 0, 1000000, 28)]
    [InlineData("Krakow", "all", 0, 1000000, int.MaxValue)]
    public async Task InvalidRequestsReturn400BeforeContactingPortals(string city, string source, int from, int to, int page)
    {
        var h = new Harness();
        Assert.IsType<BadRequestObjectResult>(await h.Controller.Get(city, source, from, to, page));
        h.Olx.VerifyNoOtherCalls();
        h.Otodom.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeduplicatesCanonicalUrlsAndPrefersOtodomWithoutConfusingPortalLocalIds()
    {
        var h = new Harness();
        var direct = new SearchData { Id = "123", Url = "https://www.otodom.pl/pl/oferta/apartment-ID123", Title = "Direct" };
        var distinct = new SearchData { Id = "123", Url = "https://www.olx.pl/d/oferta/different-ID123", Title = "Different" };
        h.Olx.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { Source = "olx", City = "Krakow", Data = new()
            {
                new() { Id = "456", Url = direct.Url + "/?utm_source=olx#map", Title = "Syndicated" }, distinct
            } });
        h.Otodom.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { Source = "otodom", City = "Krakow", Data = new() { direct } });

        var result = await h.Service.GetAsync();
        Assert.Equal(2, result.ReturnedCount);
        Assert.Same(direct, result.Data[0]);
        Assert.Contains(distinct, result.Data);
        Assert.Equal(3, result.Pages.Sum(p => p.ReturnedCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicatePartnerOfferFillsOnlyMissingOtodomDetails(bool directHasDetails)
    {
        var h = new Harness();
        var direct = new SearchData
        {
            Id = "123", Url = "https://www.otodom.pl/pl/oferta/apartment-ID123", Price = 510000,
            Market = directHasDetails ? "Pierwotny" : "",
            BuildingType = directHasDetails ? "Apartamentowiec" : "",
            Location = new LocationPlace
            {
                City = "Kraków", District = directHasDetails ? "Podgórze" : "",
                Lat = directHasDetails ? 50.02 : 0, Lon = directHasDetails ? 19.94 : 0
            }
        };
        var partner = new SearchData
        {
            Id = "456", Url = direct.Url, Price = 500000, Market = "Wtórny", BuildingType = "Blok",
            Location = new LocationPlace { City = "Kraków", District = "Prądnik Biały", Lat = 50.09, Lon = 19.92 }
        };
        h.Olx.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { Source = "olx", City = "Krakow", Data = new() { partner } });
        h.Otodom.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { Source = "otodom", City = "Krakow", Data = new() { direct } });

        var result = Assert.Single((await h.Service.GetAsync()).Data);
        Assert.Equal("123", result.Id);
        Assert.Equal(510000, result.Price);
        Assert.Equal(directHasDetails ? "Pierwotny" : "Wtórny", result.Market);
        Assert.Equal(directHasDetails ? "Apartamentowiec" : "Blok", result.BuildingType);
        Assert.Equal(directHasDetails ? "Podgórze" : "Prądnik Biały", result.Location.District);
        Assert.Equal(directHasDetails ? 50.02 : 50.09, result.Location.Lat);
        Assert.Equal(directHasDetails ? 19.94 : 19.92, result.Location.Lon);
    }

    [Fact]
    public async Task PartialFailurePreservesSuccessfulDataAndReportsSafeSourceError()
    {
        var h = new Harness();
        var offer = new SearchData { Id = "one", Url = "https://www.otodom.pl/pl/oferta/one-ID1" };
        h.Olx.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("private upstream body", null, HttpStatusCode.Forbidden));
        h.Otodom.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { Source = "otodom", City = "Krakow", HasMore = true, Data = new() { offer } });

        var response = Assert.IsType<OkObjectResult>(await h.Controller.Get());
        var result = Assert.IsType<MarketplaceListingsResponse>(response.Value);
        Assert.True(result.IsPartial);
        Assert.Same(offer, Assert.Single(result.Data));
        Assert.Equal(2, result.NextPage);
        var error = Assert.Single(result.Pages, p => p.Error != null);
        Assert.Equal("olx", error.Source);
        Assert.Equal("Krakow", error.City);
        Assert.Equal("upstream_error", error.Error!.Code);
        Assert.Equal(403, error.Error.UpstreamStatusCode);
        Assert.DoesNotContain("private", error.Error.Message);
    }

    [Theory]
    [InlineData(false, 502, "upstream_error")]
    [InlineData(true, 504, "timeout")]
    public async Task CompletePortalFailureDoesNotLookLikeSuccessfulEmptySearch(bool timeout, int status, string code)
    {
        var h = new Harness();
        Exception failure = timeout ? new TaskCanceledException("timeout") : new HttpRequestException("unavailable");
        h.Olx.Setup(x => x.GetPageAsync(It.IsAny<CityEnum>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        h.Otodom.Setup(x => x.GetPageAsync(It.IsAny<CityEnum>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var response = Assert.IsType<ObjectResult>(await h.Controller.Get());
        Assert.Equal(status, response.StatusCode);
        var result = Assert.IsType<MarketplaceListingsResponse>(response.Value);
        Assert.True(result.IsPartial);
        Assert.Empty(result.Data);
        Assert.All(result.Pages, p => Assert.Equal(code, p.Error!.Code));
    }

    [Fact]
    public async Task MalformedPortalDataReports502AndDoesNotLeakParserDetails()
    {
        var h = new Harness();
        h.Otodom.Setup(x => x.GetPageAsync(It.IsAny<CityEnum>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JsonSerializationException("unexpected upstream content"));
        var response = Assert.IsType<ObjectResult>(await h.Controller.Get(source: "otodom"));
        Assert.Equal(502, response.StatusCode);
        var error = Assert.Single(Assert.IsType<MarketplaceListingsResponse>(response.Value).Pages).Error!;
        Assert.Equal("invalid_response", error.Code);
        Assert.DoesNotContain("unexpected", error.Message);
        h.Olx.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptySuccessfulSearchReturns200()
    {
        var h = new Harness();
        h.EmptyPages();
        var response = Assert.IsType<OkObjectResult>(await h.Controller.Get());
        var result = Assert.IsType<MarketplaceListingsResponse>(response.Value);
        Assert.False(result.IsPartial);
        Assert.False(result.HasMore);
        Assert.Null(result.NextPage);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task RequestCancellationPropagatesToPortalWithoutBecoming504()
    {
        var h = new Harness();
        using var cancellation = new CancellationTokenSource();
        h.Olx.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<MarketplaceCityPage>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Controller.Get(source: "olx", cancellationToken: cancellation.Token));
        h.Olx.Verify(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 1, 36, cancellation.Token), Times.Once);
        h.Otodom.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PaginationCapIsExplicitEvenWhenPortalHasMoreData()
    {
        var h = new Harness();
        h.Otodom.Setup(x => x.GetPageAsync(CityEnum.Krakow, 50000, 10000000, 27, 36, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceCityPage { City = "Krakow", Source = "otodom", HasMore = true });
        var result = await h.Service.GetAsync(source: "otodom", page: 27);
        Assert.True(result.HasMore);
        Assert.True(result.PaginationLimitReached);
        Assert.Null(result.NextPage);
    }

    [Fact]
    public void LiveEndpointUsesExistingApiKeyGateAndDisablesResponseCaching()
    {
        Assert.NotNull(typeof(MarketplaceListingsController).GetCustomAttribute<RequireScrapeApiKeyAttribute>());
        var cache = typeof(MarketplaceListingsController).GetMethod(nameof(MarketplaceListingsController.Get))!
            .GetCustomAttribute<ResponseCacheAttribute>();
        Assert.NotNull(cache);
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
    }
}
