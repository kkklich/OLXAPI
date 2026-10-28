using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class ScrapePipelineTests
{
    private static SearchData Offer(string id) => new() { Id = id, Url = $"https://example.com/{id}", Price = 500000 };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavesHealthySourcesAndPartialOlxBeforeReportingFailure(bool partial)
    {
        var olx = new Mock<IOLXAPIService>();
        var morizon = new Mock<IMorizonApiService>();
        var nieruchomosci = new Mock<INieruchomosciOnlineService>();
        var repository = new Mock<IPropertyDataRepository>();
        var mapper = new Mock<IMapper>();
        var offers = new Mock<IOfferSnapshotCache>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var failure = partial
            ? (Exception)new PartialScrapeException("incomplete", new[] { Offer("olx") }, new Exception("network"))
            : new Exception("network");
        olx.Setup(x => x.GetOLXResponse(CityEnum.Krakow)).ThrowsAsync(failure);
        morizon.Setup(x => x.GetPropertyListingDataAsync(CityEnum.Krakow))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("morizon") } });
        nieruchomosci.Setup(x => x.GetAllPagesAsync(CityEnum.Krakow))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("nieruchomosci") } });
        mapper.Setup(x => x.Map<List<PropertyData>>(It.IsAny<object>()))
            .Returns((object input) => ((List<SearchData>)input).Select(x => new PropertyData { Url = x.Url }).ToList());
        List<PropertyData>? saved = null;
        repository.Setup(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()))
            .Callback<List<PropertyData>>(data => saved = data).Returns(Task.CompletedTask);
        var service = new RealEstateServices(olx.Object, morizon.Object, nieruchomosci.Object,
            mapper.Object, repository.Object, cache, offers.Object, NullLogger<RealEstateServices>.Instance);

        var error = await Assert.ThrowsAsync<AggregateException>(() => service.LoadDataMarkeplacesAsync());
        Assert.Contains("OLX (Krakow)", Assert.Single(error.InnerExceptions).Message);
        Assert.NotNull(saved);
        Assert.Equal(partial ? 3 : 2, saved.Count);
        Assert.All(saved, row => Assert.Equal("Krakow", row.City));
        Assert.Single(saved.Select(row => row.AddedRecordTime).Distinct());
        offers.Verify(x => x.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task ContinuesOtherCitiesAfterSourceFailure()
    {
        var olx = new Mock<IOLXAPIService>();
        var morizon = new Mock<IMorizonApiService>();
        var nieruchomosci = new Mock<INieruchomosciOnlineService>();
        var repository = new Mock<IPropertyDataRepository>();
        var mapper = new Mock<IMapper>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        olx.Setup(x => x.GetOLXResponse(It.IsAny<CityEnum>())).ReturnsAsync(new MarketplaceSearch());
        morizon.Setup(x => x.GetPropertyListingDataAsync(It.IsAny<CityEnum>()))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("morizon") } });
        nieruchomosci.Setup(x => x.GetAllPagesAsync(It.IsAny<CityEnum>()))
            .ReturnsAsync(new MarketplaceSearch { Data = new() { Offer("nieruchomosci") } });
        mapper.Setup(x => x.Map<List<PropertyData>>(It.IsAny<object>()))
            .Returns((object input) => ((List<SearchData>)input).Select(x => new PropertyData { Url = x.Url }).ToList());
        repository.Setup(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>())).Returns(Task.CompletedTask);
        var service = new RealEstateServices(olx.Object, morizon.Object, nieruchomosci.Object,
            mapper.Object, repository.Object, cache, Mock.Of<IOfferSnapshotCache>(), NullLogger<RealEstateServices>.Instance);

        var error = await Assert.ThrowsAsync<AggregateException>(() => service.GetdataForManyCitiesAsync());
        // Only Krakow and Katowice have configured OLX ids; intentional skips are not failures.
        Assert.Equal(2, error.Flatten().InnerExceptions.Count);
        repository.Verify(x => x.SaveMarketplaceDataAsync(It.IsAny<List<PropertyData>>()), Times.Exactly(7));
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
