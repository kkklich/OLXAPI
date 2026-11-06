using System.Net;
using AF_mobile_web_api.Controllers;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Mappings;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class WeeklyOlxSaveTests
{
    [Fact]
    public async Task WeeklyTriggerDownloadsMapsAndSavesOlxForAllSevenCities()
    {
        // Exercise the real controller, background runner, OLX downloader, mapper and
        // save orchestration. Only the external HTTP and repository boundaries are faked.
        var cityIds = new Dictionary<CityEnum, int>
        {
            [CityEnum.Krakow] = 8959, [CityEnum.Katowice] = 7691,
            [CityEnum.Chorzow] = 22611, [CityEnum.Tychy] = 17705,
            [CityEnum.Mikolow] = 11631, [CityEnum.Myslowice] = 50917,
            [CityEnum.Sosnowiec] = 16027
        };
        var http = new Mock<IHTTPClientServices>(MockBehavior.Strict);
        http.Setup(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()))
            .Returns((string url, double? timeout, IDictionary<string, string> headers) =>
            {
                var query = QueryHelpers.ParseQuery(new Uri(url).Query);
                var cityId = int.Parse(query["city_id"].ToString());
                var city = cityIds.Single(pair => pair.Value == cityId).Key;
                Assert.Equal("0", query["offset"].ToString());
                Assert.Equal("50000", query["filter_float_price:from"].ToString());
                Assert.Equal("10000000", query["filter_float_price:to"].ToString());
                var json = JsonConvert.SerializeObject(new
                {
                    data = new[] { new
                    {
                        id = cityId, url = $"https://www.olx.pl/d/oferta/weekly-{cityId}.html",
                        title = $"Mieszkanie {city.ToDisplayName()}", business = false,
                        @params = new[] {
                            new { key = "price", value = new { label = "500 000 zł", key = "" } },
                            new { key = "m", value = new { label = "50 m²", key = "50" } },
                            new { key = "market", value = new { label = "Wtórny", key = "secondary" } },
                            new { key = "builttype", value = new { label = "Blok", key = "blok" } },
                            new { key = "floor_select", value = new { label = "2", key = "floor_2" } }
                        },
                        location = new { city = new { id = cityId, name = city.ToDisplayName() },
                            district = new { name = "Centrum" } },
                        map = new { lat = 50.25, lon = 19.05 }
                    } },
                    metadata = new { total_elements = 1, visible_total_count = 1 }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            });
        var olx = new OLXAPIService(http.Object, NullLogger<OLXAPIService>.Instance,
            Options.Create(new OlxScrapeOptions { InitialBucketSize = 10_000_000, MaxAttempts = 1 }));
        var morizon = new Mock<IMorizonApiService>();
        morizon.Setup(x => x.GetPropertyListingDataAsync(It.IsAny<CityEnum>()))
            .Returns((CityEnum city, string? searchUrl) => Task.FromResult(OtherSource(city, WebName.Morizon)));
        var online = new Mock<INieruchomosciOnlineService>();
        online.Setup(x => x.GetAllPagesAsync(It.IsAny<CityEnum>()))
            .ReturnsAsync((CityEnum city) => OtherSource(city, WebName.NieruchomsciOnline));
        var batches = new List<List<PropertyData>>();
        var repository = new Mock<IPropertyDataRepository>(MockBehavior.Strict);
        repository.Setup(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()))
            .Callback<List<PropertyData>>(rows => batches.Add(rows)).Returns(Task.CompletedTask);
        var snapshots = new Mock<IOfferSnapshotCache>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var mapper = new MapperConfiguration(config => config.AddProfile<MappingProfile>()).CreateMapper();
        var realEstate = new RealEstateServices(olx, morizon.Object, online.Object, mapper,
            repository.Object, new ReadCache(cache), snapshots.Object, NullLogger<RealEstateServices>.Instance);
        using var services = new ServiceCollection().AddSingleton<IRealEstateServices>(realEstate).BuildServiceProvider();
        var runner = new ScrapeJobRunner(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScrapeJobRunner>.Instance);
        var controller = new RealEstateController(realEstate, Mock.Of<IStatisticServices>(),
            Mock.Of<IPropertyListService>(), runner, Mock.Of<IMapPointsPayloadProvider>(), NullLogger<RealEstateController>.Instance);
        var startedAt = DateTime.UtcNow;

        Assert.IsType<AcceptedResult>(controller.GetdataForManyCities());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (runner.IsRunning) await Task.Delay(10, timeout.Token);

        Assert.True(runner.Status.LastRunSucceeded, runner.Status.LastError);
        Assert.Equal(7, batches.Count);
        foreach (var city in cityIds.Keys)
        {
            var batch = Assert.Single(batches, rows => rows.All(row => row.City == city.ToString()));
            var savedOlx = Assert.Single(batch, row => row.WebName == (int)WebName.OLX);
            Assert.Equal($"https://www.olx.pl/d/oferta/weekly-{cityIds[city]}.html", savedOlx.Url);
            Assert.Equal($"Mieszkanie {city.ToDisplayName()}", savedOlx.Title);
            Assert.Equal(500000, savedOlx.Price);
            Assert.Equal(50, savedOlx.Area);
            Assert.Equal(10000, savedOlx.PricePerMeter);
            Assert.Equal("Wtórny", savedOlx.Market);
            Assert.Equal("Blok", savedOlx.BuildingType);
            Assert.Equal(2, savedOlx.Floor);
            Assert.True(savedOlx.Private);
            Assert.Equal("Centrum", savedOlx.District);
            Assert.Equal(50.25, savedOlx.Lat);
            Assert.Equal(19.05, savedOlx.Lon);
            Assert.Single(batch.Select(row => row.AddedRecordTime).Distinct());
            Assert.InRange(savedOlx.AddedRecordTime, startedAt, DateTime.UtcNow);
        }
        http.Verify(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()), Times.Exactly(7));
        repository.Verify(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()), Times.Exactly(7));
        repository.VerifyNoOtherCalls();
        snapshots.Verify(x => x.Invalidate(), Times.Once);
    }

    private static MarketplaceSearch OtherSource(CityEnum city, WebName source) => new()
    {
        Data = new() { new SearchData
        {
            Id = $"{source}-{city}", Url = $"https://example.com/{source}/{city}",
            Title = "Other source", Price = 600000, Area = 60, WebName = source,
            Location = new LocationPlace { City = city.ToDisplayName(), District = "Centrum" }
        } }
    };
}
