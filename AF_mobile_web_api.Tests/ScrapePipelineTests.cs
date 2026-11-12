using AF_mobile_web_api.Domain;
using AF_mobile_web_api.Controllers;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class ScrapePipelineTests
{
    private static SearchData Offer(string id) => new() { Id = id, Url = $"https://example.com/{id}", Price = 500000 };

    [Fact]
    public async Task RepeatedUniqueOfferReadsDoNotAppendUrlsToTheCachedBatch()
    {
        var repository = new Mock<IPropertyDataRepository>();
        repository.Setup(x => x.GetLatestByCityAsync("Krakow")).ReturnsAsync(new List<PropertyData>
        {
            new() { Id = Guid.NewGuid(), Url = "/one", Price = 500000, Area = 50, Market = "Wtorny", Floor = 2 },
            new() { Id = Guid.NewGuid(), Url = "/two", Price = 500000, Area = 50, Market = "Wtorny", Floor = 2 }
        });
        using var memory = new MemoryCache(new MemoryCacheOptions());
        using var historyCache = new PropertyHistoryCache();
        var service = new RealEstateServices(Mock.Of<IOLXAPIService>(), Mock.Of<IMorizonApiService>(),
            Mock.Of<INieruchomosciOnlineService>(), repository.Object, new ReadCache(memory),
            Mock.Of<IOfferSnapshotCache>(), historyCache, NullLogger<RealEstateServices>.Instance);

        Assert.Equal("/one, /two", Assert.Single(await service.GetUniqueOffertsAsync()).Url);
        Assert.Equal("/one, /two", Assert.Single(await service.GetUniqueOffertsAsync()).Url);
        Assert.Equal(new[] { "/one", "/two" }, (await service.GetLatestBatchAsync(CityEnum.Krakow)).Select(x => x.Url));
        repository.Verify(x => x.GetLatestByCityAsync("Krakow"), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavesHealthySourcesAndPartialOlxBeforeReportingFailure(bool partial)
    {
        var olx = new Mock<IOLXAPIService>();
        var morizon = new Mock<IMorizonApiService>();
        var nieruchomosci = new Mock<INieruchomosciOnlineService>();
        var repository = new Mock<IPropertyDataRepository>();
        var offers = new Mock<IOfferSnapshotCache>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var historyCache = new PropertyHistoryCache();
        var failure = partial
            ? (Exception)new PartialScrapeException("incomplete", new[] { Offer("olx") }, new Exception("network"))
            : new Exception("network");
        olx.Setup(x => x.GetOLXResponse(CityEnum.Krakow)).ThrowsAsync(failure);
        morizon.Setup(x => x.GetPropertyListingDataAsync(CityEnum.Krakow))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("morizon") } });
        nieruchomosci.Setup(x => x.GetAllPagesAsync(CityEnum.Krakow))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("nieruchomosci") } });
        List<PropertyData>? saved = null;
        repository.Setup(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()))
            .Callback<List<PropertyData>>(data => saved = data).Returns(Task.CompletedTask);
        var service = new RealEstateServices(olx.Object, morizon.Object, nieruchomosci.Object,
            repository.Object, new ReadCache(cache), offers.Object, historyCache, NullLogger<RealEstateServices>.Instance);
        await historyCache.GetAsync("Krakow", "/flat", _ => Task.FromResult<AF_mobile_web_api.DTO.PropertyHistoryDTO?>(new() { Title = "old" }));

        var error = await Assert.ThrowsAsync<AggregateException>(() => service.LoadDataMarkeplacesAsync());
        Assert.Contains("OLX (Krakow)", Assert.Single(error.InnerExceptions).Message);
        Assert.NotNull(saved);
        Assert.Equal(partial ? 3 : 2, saved.Count);
        Assert.All(saved, row => Assert.Equal("Krakow", row.City));
        Assert.Single(saved.Select(row => row.AddedRecordTime).Distinct());
        offers.Verify(x => x.Invalidate(), Times.Once);
        Assert.Equal("new", (await historyCache.GetAsync("Krakow", "/flat",
            _ => Task.FromResult<AF_mobile_web_api.DTO.PropertyHistoryDTO?>(new() { Title = "new" })))!.Title);
    }

    [Fact]
    public async Task ContinuesOtherCitiesAfterSourceFailure()
    {
        var olx = new Mock<IOLXAPIService>();
        var morizon = new Mock<IMorizonApiService>();
        var nieruchomosci = new Mock<INieruchomosciOnlineService>();
        var repository = new Mock<IPropertyDataRepository>();
        var snapshots = new Mock<IOfferSnapshotCache>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var historyCache = new PropertyHistoryCache();
        foreach (var area in new[] { "Krakow", "Silesia", "Katowice" })
        {
            cache.Set(MapPointsPayloadProvider.CacheKey(area), new object());
            cache.Set(MapPointsPayloadProvider.CacheKey(area, compact: true), new object());
        }
        olx.Setup(x => x.GetOLXResponse(It.IsAny<CityEnum>())).ReturnsAsync(new MarketplaceSearch());
        morizon.Setup(x => x.GetPropertyListingDataAsync(It.IsAny<CityEnum>()))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("morizon") } });
        nieruchomosci.Setup(x => x.GetAllPagesAsync(It.IsAny<CityEnum>()))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("nieruchomosci") } });
        repository.Setup(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>())).Returns(Task.CompletedTask);
        var service = new RealEstateServices(olx.Object, morizon.Object, nieruchomosci.Object,
            repository.Object, new ReadCache(cache), snapshots.Object, historyCache, NullLogger<RealEstateServices>.Instance);

        var error = await Assert.ThrowsAsync<AggregateException>(() => service.GetdataForManyCitiesAsync());
        // OLX is required for every configured city, so an empty response fails each city
        // after the healthy sources have still been saved.
        Assert.Equal(7, error.Flatten().InnerExceptions.Count);
        repository.Verify(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()), Times.Exactly(7));
        snapshots.Verify(x => x.Invalidate(), Times.Once);
        foreach (var area in new[] { "Krakow", "Silesia", "Katowice" })
        {
            Assert.False(cache.TryGetValue(MapPointsPayloadProvider.CacheKey(area), out _));
            Assert.False(cache.TryGetValue(MapPointsPayloadProvider.CacheKey(area, compact: true), out _));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WeeklyRunResumesAcrossRunnerRestartsAndKeepsSourceFailures(bool partial)
    {
        const long runId = 41326903210;
        var saved = new Dictionary<string, WeeklyScrapeCity>();
        var repository = new Mock<IPropertyDataRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetWeeklyCitiesAsync(runId)).ReturnsAsync(() => saved.Values.ToList());
        repository.Setup(r => r.SaveWeeklyCityAsync(It.IsAny<List<PropertyData>>(), It.IsAny<WeeklyScrapeCity>()))
            .Callback<List<PropertyData>, WeeklyScrapeCity>((rows, result) =>
            {
                Assert.Equal(runId, result.RunId);
                Assert.NotEmpty(rows);
                Assert.All(rows, p => Assert.Equal(result.City, p.City));
                saved.Add(result.City, result);
            }).Returns(Task.CompletedTask);

        var data = new MarketplaceSearch { Data = new() { Offer("listing") } };
        var olx = new Mock<IOLXAPIService>();
        olx.Setup(s => s.GetOLXResponse(It.IsAny<CityEnum>())).ReturnsAsync(data);
        if (partial) olx.Setup(s => s.GetOLXResponse(CityEnum.Krakow)).ThrowsAsync(new Exception("private detail"));
        var morizon = new Mock<IMorizonApiService>();
        morizon.Setup(s => s.GetPropertyListingDataAsync(It.IsAny<CityEnum>())).ReturnsAsync(data);
        var nieruchomosci = new Mock<INieruchomosciOnlineService>();
        nieruchomosci.Setup(s => s.GetAllPagesAsync(It.IsAny<CityEnum>())).ReturnsAsync(data);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        using var history = new PropertyHistoryCache();
        var service = new RealEstateServices(olx.Object, morizon.Object, nieruchomosci.Object, repository.Object,
            new ReadCache(memory), Mock.Of<IOfferSnapshotCache>(), history, NullLogger<RealEstateServices>.Instance);
        using var services = new ServiceCollection().AddSingleton<IRealEstateServices>(service).BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cities = Enum.GetValues<CityEnum>();
        for (var i = 0; i <= cities.Length; i++)
        {
            // Every poll hits a fresh runner, as if Render restarted after every city.
            var runner = new ScrapeJobRunner(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScrapeJobRunner>.Instance);
            var controller = new RealEstateController(service, null!, null!, runner, null!, NullLogger<RealEstateController>.Instance);
            var result = await controller.AdvanceWeeklyScrape(runId, repository.Object);
            if (i == cities.Length)
            {
                if (partial) Assert.IsType<UnprocessableEntityObjectResult>(result);
                else Assert.IsType<OkObjectResult>(result);
                break;
            }
            Assert.IsType<AcceptedResult>(result);
            while (runner.IsRunning) await Task.Delay(5, timeout.Token);
            Assert.True(saved.ContainsKey(cities[i].ToString()));
        }
        Assert.Equal(partial ? "OLX (Krakow): download incomplete." : null, saved["Krakow"].Error);
        await service.ScrapeWeeklyCityAsync(runId, CityEnum.Krakow);
        olx.Verify(s => s.GetOLXResponse(CityEnum.Krakow), Times.Once);
    }

    [Fact]
    public async Task WeeklyRunRetriesUncommittedCityOnNewAttempt()
    {
        const long runId = 52;
        var saved = new List<WeeklyScrapeCity> { new() { RunId = runId, City = "Krakow" } };
        var repository = new Mock<IPropertyDataRepository>();
        repository.Setup(r => r.GetWeeklyCitiesAsync(runId)).ReturnsAsync(saved);
        var scrape = new Mock<IRealEstateServices>();
        scrape.SetupSequence(s => s.ScrapeWeeklyCityAsync(runId, CityEnum.Katowice))
            .ThrowsAsync(new IOException("write failed")).Returns(Task.CompletedTask);
        using var services = new ServiceCollection().AddSingleton(scrape.Object).BuildServiceProvider();
        var runner = new ScrapeJobRunner(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScrapeJobRunner>.Instance);
        var controller = new RealEstateController(scrape.Object, null!, null!, runner, null!, NullLogger<RealEstateController>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.IsType<AcceptedResult>(await controller.AdvanceWeeklyScrape(runId, repository.Object));
        while (runner.IsRunning) await Task.Delay(5, timeout.Token);
        Assert.IsType<ConflictObjectResult>(await controller.AdvanceWeeklyScrape(runId, repository.Object));
        Assert.IsType<AcceptedResult>(await controller.AdvanceWeeklyScrape(runId, repository.Object, attempt: 2));
        while (runner.IsRunning) await Task.Delay(5, timeout.Token);
        scrape.Verify(s => s.ScrapeWeeklyCityAsync(runId, CityEnum.Katowice), Times.Exactly(2));
        scrape.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunnerSerializesJobsAndReportsTheirActualOutcome(bool succeeds)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var runner = new ScrapeJobRunner(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScrapeJobRunner>.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(runner.TryStart("test", async (IServiceProvider _) => {
            await release.Task;
            if (!succeeds) throw new AggregateException(new ScrapeSourceException("OLX", CityEnum.Krakow, new Exception("secret body")));
        }));
        Assert.True(runner.Status.IsRunning);
        Assert.False(runner.TryStart("overlap", (IServiceProvider _) => Task.CompletedTask));
        release.SetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (runner.IsRunning) await Task.Delay(10, timeout.Token);
        var status = runner.Status;
        Assert.Equal(succeeds, status.LastRunSucceeded);
        Assert.NotNull(status.LastCompletedAt);
        if (!succeeds) {
            Assert.Contains("OLX (Krakow)", status.LastError);
            Assert.DoesNotContain("secret", status.LastError);
        }
    }
}
