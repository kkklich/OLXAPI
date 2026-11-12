using System.Net;
using System.Text.Json;
using AF_mobile_web_api.Controllers;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class PropertyHistoryTests
{
    [Fact]
    public async Task ConcurrentRequestsShareOneBuild()
    {
        using var cache = new PropertyHistoryCache();
        var finish = new TaskCompletionSource<PropertyHistoryDTO?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var requests = Enumerable.Range(0, 20).Select(_ => cache.GetAsync("Krakow", "/flat", ct =>
        {
            Interlocked.Increment(ref calls);
            return finish.Task;
        })).ToArray();
        Assert.Equal(1, calls);
        var history = new PropertyHistoryDTO { Title = "same history" };
        finish.SetResult(history);
        Assert.All(await Task.WhenAll(requests), result => Assert.Same(history, result));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExactCityAndUrlRemainSeparateIncludingCaseAndQueryString()
    {
        using var cache = new PropertyHistoryCache();
        var requests = new[] { ("Krakow", "/flat"), ("Katowice", "/flat"), ("Krakow", "/Flat"), ("Krakow", "/flat?x=1") };
        foreach (var (city, url) in requests)
        {
            var history = await cache.GetAsync(city, url, _ => Task.FromResult<PropertyHistoryDTO?>(new() { Title = city + url }));
            Assert.Equal(city + url, history!.Title);
        }
        foreach (var (city, url) in requests)
            Assert.Equal(city + url, (await cache.GetAsync(city, url, _ => throw new Exception("unexpected query")))!.Title);
    }

    [Fact]
    public async Task InvalidationDuringBuildPreventsStaleCacheRestore()
    {
        using var cache = new PropertyHistoryCache();
        var finish = new TaskCompletionSource<PropertyHistoryDTO?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = cache.GetAsync("Krakow", "/flat", _ => finish.Task);
        cache.Invalidate();
        finish.SetResult(new() { Title = "old" });
        Assert.Equal("old", (await request)!.Title);
        Assert.Equal("new", (await cache.GetAsync("Krakow", "/flat", _ => Task.FromResult<PropertyHistoryDTO?>(new() { Title = "new" })))!.Title);
    }

    [Fact]
    public async Task MissingOffersAreCachedUntilInvalidationButFailuresAreRetried()
    {
        using var cache = new PropertyHistoryCache();
        Assert.Null(await cache.GetAsync("Krakow", "/missing", _ => Task.FromResult<PropertyHistoryDTO?>(null)));
        Assert.Null(await cache.GetAsync("Krakow", "/missing", _ => throw new Exception("unexpected query")));
        cache.Invalidate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("Krakow", "/missing", _ => throw new InvalidOperationException()));
        Assert.NotNull(await cache.GetAsync("Krakow", "/missing", _ => Task.FromResult<PropertyHistoryDTO?>(new())));
    }

    [Fact]
    public async Task OversizeHistoryIsReturnedWithoutRetainingIt()
    {
        using var cache = new PropertyHistoryCache(sizeLimit: 1024);
        var calls = 0;
        Task<PropertyHistoryDTO?> Build(CancellationToken _) => Task.FromResult<PropertyHistoryDTO?>(new() { Title = (++calls).ToString(), Description = new string('x', 1024) });
        Assert.Equal("1", (await cache.GetAsync("Krakow", "/flat", Build))!.Title);
        Assert.Equal("2", (await cache.GetAsync("Krakow", "/flat", Build))!.Title);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCancelAnotherVisitorsBuild()
    {
        using var cache = new PropertyHistoryCache();
        var finish = new TaskCompletionSource<PropertyHistoryDTO?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetAsync("Krakow", "/flat", _ => finish.Task);
        using var cancelled = new CancellationTokenSource();
        var waiter = cache.GetAsync("Krakow", "/flat", _ => throw new Exception("unexpected query"), cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        finish.SetResult(new() { Title = "complete" });
        Assert.Equal("complete", (await first)!.Title);
        Assert.Equal("complete", (await cache.GetAsync("Krakow", "/flat", _ => throw new Exception("unexpected query")))!.Title);
    }

    [Fact]
    public async Task CancelledBuildIsNotCachedAndTheNextRequestCanRetry()
    {
        using var cache = new PropertyHistoryCache();
        using var cancelled = new CancellationTokenSource();
        var request = cache.GetAsync("Krakow", "/flat", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new PropertyHistoryDTO();
        }, cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.NotNull(await cache.GetAsync("Krakow", "/flat", _ => Task.FromResult<PropertyHistoryDTO?>(new())));
    }

    [Fact]
    public async Task ServicePropagatesCancellationAndCachesTheExistingHistoryContract()
    {
        using var cache = new PropertyHistoryCache();
        using var cancellation = new CancellationTokenSource();
        var repo = new Mock<IPropertyDataRepository>(MockBehavior.Strict);
        var target = new PropertyData { Url = "/flat", City = "Krakow", Area = 50, Price = 500000, AddedRecordTime = new DateTime(2026, 10, 30) };
        var previous = new PropertyData { Url = "/flat", City = "Krakow", Area = 50, Price = 550000, AddedRecordTime = new DateTime(2026, 10, 23) };
        var rows = new List<PropertyData> { target, previous };
        repo.Setup(x => x.GetLatestByUrlAsync("Krakow", "/flat", cancellation.Token)).ReturnsAsync(target);
        repo.Setup(x => x.GetHistoryCandidatesAsync("Krakow", It.IsAny<HistoryCandidateFilter>(), cancellation.Token)).ReturnsAsync(rows);
        var comparer = new Mock<IPropertyComparer>();
        comparer.Setup(x => x.FindMatches(target, rows)).Returns(rows);
        var service = new PropertyListService(repo.Object, comparer.Object, Mock.Of<IOfferSnapshotCache>(), cache);
        var history = await service.GetHistoryAsync("Krakow", "/flat", cancellation.Token);
        Assert.Same(history, await service.GetHistoryAsync("Krakow", "/flat", cancellation.Token));
        Assert.Equal(550000, history!.FirstPrice);
        Assert.Equal(-50000, history.TotalPriceChange);
        Assert.Equal(new[] { 550000d, 500000d }, history.Entries.Select(x => x.Price));
        repo.Verify(x => x.GetLatestByUrlAsync("Krakow", "/flat", cancellation.Token), Times.Once);
        repo.Verify(x => x.GetHistoryCandidatesAsync("Krakow", It.IsAny<HistoryCandidateFilter>(), cancellation.Token), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HttpHistoryLimitReturns429AndRetryAfterWithoutLimitingOtherEndpoints()
    {
        // Exercise the actual controller attribute and production registration without
        // starting the production app, reading its settings, or connecting to any DB.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PropertyHistory:PermitLimit"] = "1",
            ["PropertyHistory:WindowSeconds"] = "60"
        });
        builder.Services.AddRouting();
        builder.Services.AddControllers().AddApplicationPart(typeof(RealEstateController).Assembly);
        builder.Services.AddPropertyHistoryRateLimiting(builder.Configuration);
        var list = new Mock<IPropertyListService>();
        list.Setup(x => x.GetHistoryAsync("Krakow", "/flat", It.IsAny<CancellationToken>())).ReturnsAsync(new PropertyHistoryDTO { Title = "test history" });
        builder.Services.AddSingleton(list.Object);
        builder.Services.AddSingleton(Mock.Of<IRealEstateServices>());
        builder.Services.AddSingleton(Mock.Of<IStatisticServices>());
        builder.Services.AddSingleton(Mock.Of<IScrapeJobRunner>());
        builder.Services.AddSingleton(Mock.Of<IMapPointsPayloadProvider>());
        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapGet("/health-test", () => "ok");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
            using var first = await client.GetAsync("/api/RealEstate/propertyHistory/Krakow?url=%2Fflat");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.Equal("test history", json.RootElement.GetProperty("title").GetString());
            using var excess = await client.GetAsync("/api/RealEstate/propertyHistory/Katowice?url=%2Fother");
            Assert.Equal(HttpStatusCode.TooManyRequests, excess.StatusCode);
            Assert.InRange(excess.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
            using var health = await client.GetAsync("/health-test");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            list.Verify(x => x.GetHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await app.StopAsync(); }
    }
}
