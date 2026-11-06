using System.IO.Compression;
using System.Text.Json;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class PerformanceTests
{
    [Fact]
    public async Task ConcurrentCacheMissesShareOneBuild()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new ReadCache(memory);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var requests = Enumerable.Range(0, 20).Select(_ => cache.GetAsync("test", TimeSpan.FromMinutes(1), () =>
        {
            Interlocked.Increment(ref calls);
            return finish.Task;
        })).ToArray();
        Assert.Equal(1, calls);
        finish.SetResult("same batch");
        Assert.All(await Task.WhenAll(requests), value => Assert.Equal("same batch", value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvalidatedBuildCannotRestoreOldDataAndFailuresAreRetried()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new ReadCache(memory);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = cache.GetAsync("test", TimeSpan.FromMinutes(1), () => finish.Task);
        cache.Remove("test");
        finish.SetResult("old");
        Assert.Equal("old", await old);
        Assert.False(memory.TryGetValue("test", out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync<string>("test", TimeSpan.FromMinutes(1),
            () => throw new InvalidOperationException()));
        Assert.Equal("new", await cache.GetAsync("test", TimeSpan.FromMinutes(1), () => Task.FromResult("new")));
    }

    [Fact]
    public async Task SnapshotRefreshServesThePreviousCompleteBatch()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async IAsyncEnumerable<LatestOfferRow> Rows()
        {
            var batch = Interlocked.Increment(ref calls);
            if (batch == 2)
            {
                refreshStarted.SetResult();
                await release.Task;
            }
            yield return new LatestOfferRow { Id = Guid.NewGuid(), Title = $"batch {batch}", Url = $"/{batch}" };
        }
        var repo = new Mock<IPropertyDataRepository>();
        repo.Setup(x => x.StreamLatestOffersAsync()).Returns(Rows);
        using var provider = new ServiceCollection().AddSingleton(repo.Object).BuildServiceProvider();
        var cache = new OfferSnapshotCache(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OfferSnapshotCache>.Instance);
        var first = await cache.GetAsync();
        cache.Invalidate();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var readDuringRefresh = cache.GetAsync();
        Assert.True(readDuringRefresh.IsCompletedSuccessfully);
        Assert.Same(first, await readDuringRefresh);
        release.SetResult();
        IReadOnlyList<OfferSnapshot> current = first;
        for (var i = 0; i < 100 && ReferenceEquals(current, first); i++)
        {
            await Task.Delay(10);
            current = await cache.GetAsync();
        }
        Assert.Equal("batch 2", Assert.Single(current).Title);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("asc", 100, 300)]
    [InlineData("desc", 300, 100)]
    public void IndexedSortKeepsUnknownPricesLastAndPagesStable(string direction, double first, double second)
    {
        var builder = new OfferSnapshotBuilder();
        var offers = new[] { 0d, 300, 100 }.Select((price, i) => builder.Build(new LatestOfferRow
        {
            Id = new Guid(i, 0, 0, new byte[8]), Price = price, City = "Krakow", Title = "Łąka", Url = $"/{i}"
        })).ToArray();
        PropertyQueryParams Query(int page) => new() { City = "kraków", SortBy = "PRICE", SortDir = direction, Page = page, PageSize = 1 };
        Assert.Equal(first, Assert.Single(OfferQuery.Apply(offers, Query(1)).Items).Price);
        Assert.Equal(second, Assert.Single(OfferQuery.Apply(offers, Query(2)).Items).Price);
        Assert.Equal(0, Assert.Single(OfferQuery.Apply(offers, Query(3)).Items).Price);
        var filtered = Query(1);
        filtered.PriceMax = 200;
        Assert.Equal(100, Assert.Single(OfferQuery.Apply(offers, filtered).Items).Price);
        Assert.Equal(1, OfferQuery.Apply(offers, filtered).TotalCount);
        Assert.Empty(OfferQuery.Apply(offers, Query(int.MaxValue)).Items);
    }

    [Fact]
    public async Task ListPageUsesSnapshotDisplayFieldsWithoutAnotherDatabaseRead()
    {
        var snapshot = new Mock<IOfferSnapshotCache>();
        snapshot.Setup(x => x.GetAsync()).ReturnsAsync(new[] { new OfferSnapshot
            { Id = Guid.NewGuid(), Url = "/flat", Title = "Ładne mieszkanie" } });
        var repository = new Mock<IPropertyDataRepository>(MockBehavior.Strict);
        using var historyCache = new PropertyHistoryCache();
        var service = new PropertyListService(repository.Object, Mock.Of<IPropertyComparer>(), snapshot.Object, historyCache);
        var page = await service.GetPagedAsync(new PropertyQueryParams());
        Assert.Equal("Ładne mieszkanie", Assert.Single(page.Items).Title);
        Assert.Equal("/flat", page.Items[0].Url);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SilesiaDashboardDoesNotChangeDistrictsInCityCache()
    {
        var cityOffer = new SearchData { Id = "1", Url = "/1", Title = "Mieszkanie", Price = 500000,
            Area = 50, PricePerMeter = 10000, Market = "Wtórny", BuildingType = "Blok",
            Location = new() { City = "Katowice", District = "Centrum", Lat = 50.2, Lon = 19 } };
        var realEstate = new Mock<IRealEstateServices>();
        realEstate.Setup(x => x.GetDataAsync(It.IsAny<string>())).ReturnsAsync(new MarketplaceSearch());
        realEstate.Setup(x => x.GetDataAsync("Katowice")).ReturnsAsync(new MarketplaceSearch { Data = new() { cityOffer } });
        var repo = new Mock<IPropertyDataRepository>();
        repo.Setup(x => x.GetTimelineByCitiesAsync(It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(new List<TimelineGroup>());
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var statistics = new StatisticServices(realEstate.Object, new ReadCache(memory), repo.Object);
        var points = await statistics.GetMapPoints("Silesia");
        Assert.Contains("Katowice", Assert.Single(points).Location.District);
        Assert.Equal("Centrum", cityOffer.Location.District);
    }

    [Fact]
    public async Task CompactMapSupportsAreasAndUsesSeparateCompressedCacheAndEntityTag()
    {
        var points = Enumerable.Range(0, 100).Select(i => new MapPointDTO
        {
            Url = $"https://example.com/{i}", Title = "Mieszkanie Łąka", Price = 645795.50,
            PricePerMeter = 12915.91, Floor = 0, Area = 50.001, Market = "Wtórny", BuildingType = "Blok",
            Location = new() { Lat = 50.03123456789, Lon = 19.9123456789, District = "Łagiewniki" }
        }).ToList();
        var statistics = new Mock<IStatisticServices>();
        statistics.Setup(x => x.GetMapPoints("Silesia")).ReturnsAsync(points);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var provider = new MapPointsPayloadProvider(statistics.Object, new ReadCache(memory), Options.Create(new JsonOptions()),
            NullLogger<MapPointsPayloadProvider>.Instance);
        var original = await provider.GetAsync("silesia");
        var compact = await provider.GetAsync("SILESIA", compact: true);
        Assert.Same(compact, await provider.GetAsync("Silesia", compact: true));
        Assert.True(compact.JsonLength < original.JsonLength * .7);
        Assert.NotEqual(original.ETag, compact.ETag);
        using var gzip = new GZipStream(new MemoryStream(compact.Gzip), CompressionMode.Decompress);
        using var decoded = await JsonDocument.ParseAsync(gzip);
        var row = decoded.RootElement.GetProperty("points")[0];
        Assert.Equal(645795.50, row[2].GetDouble());
        Assert.Equal(50.03123456789, row[9].GetDouble());
        Assert.Equal("Łagiewniki", decoded.RootElement.GetProperty("districts")[row[11].GetInt32()].GetString());
        statistics.Verify(x => x.GetMapPoints("Silesia"), Times.Exactly(2));
    }
}
