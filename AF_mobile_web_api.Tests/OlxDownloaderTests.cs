using System.Net;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class OlxDownloaderTests
{
    private static OLXAPIService Create(Func<string, Task<HttpResponseMessage>> handler,
        int concurrency = 2, int attempts = 3)
    {
        var http = new Mock<IHTTPClientServices>();
        http.Setup(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()))
            .Returns((string url, double? timeout, IDictionary<string, string> headers) => handler(url));
        return new OLXAPIService(http.Object, NullLogger<OLXAPIService>.Instance,
            Options.Create(new OlxScrapeOptions {
                MaxConcurrentBuckets = concurrency, MaxAttempts = attempts,
                RetryDelayMilliseconds = 0, PageDelayMilliseconds = 0
            }));
    }

    private static HttpResponseMessage Page(int start, int count, string title = "Apartment", int? total = null) =>
        Json(new {
            data = Enumerable.Range(start, count).Select(id => new {
                id = id.ToString(), url = $"https://www.olx.pl/d/oferta/{id}", title,
                @params = new[] {
                    new { key = "price", value = new { label = "500\u00a0000 zł", key = "" } },
                    new { key = "m", value = new { label = "", key = "50" } }
                }
            }),
            metadata = new { visible_total_count = total ?? 0 }
        });
    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(value)) };

    [Fact]
    public async Task LimitsConcurrentPriceBuckets()
    {
        int active = 0, max = 0, calls = 0;
        var service = Create(async _ => {
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = max; } while (current > previous && Interlocked.CompareExchange(ref max, current, previous) != previous);
            await Task.Delay(25);
            Interlocked.Decrement(ref active);
            return Page(Interlocked.Increment(ref calls), 1);
        });
        var offers = await service.GetAllOffersAsync(14, 4, 8959, 500_000, 799_999);
        Assert.Equal(6, calls);
        Assert.Equal(6, offers.Count);
        Assert.InRange(max, 1, 2);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(408)]
    public async Task RetriesTemporaryHttpFailures(int status)
    {
        int calls = 0;
        var service = Create(_ => {
            if (++calls == 1) throw new UpstreamHttpException("temporary", (HttpStatusCode)status, TimeSpan.Zero);
            return Task.FromResult(Page(1, 1));
        });
        Assert.Single(await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HonorsRetryAfter()
    {
        int calls = 0;
        var service = Create(_ => {
            if (++calls == 1) throw new UpstreamHttpException("busy", HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(50));
            return Task.FromResult(Page(1, 1));
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Single(await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
        Assert.True(watch.ElapsedMilliseconds >= 40);
    }

    [Fact]
    public async Task ReportsTruncationWhenTotalExceedsPaginationCap()
    {
        int calls = 0;
        var service = Create(_ => Task.FromResult(Json(new {
            data = Enumerable.Range(Interlocked.Increment(ref calls) * 40, 40)
                .Select(id => new { id = id.ToString(), url = $"https://www.olx.pl/d/{id}" }),
            metadata = new { visible_total_count = 1000, total_elements = 1200 }
        })));
        var error = await Assert.ThrowsAsync<PartialScrapeException>(() =>
            service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
        Assert.Equal(1000, error.Offers.Count);
        Assert.Equal(25, calls);
    }

    [Fact]
    public async Task DoesNotRetryOrStartQueuedBucketsWhenAccessIsDenied()
    {
        int calls = 0;
        var service = Create(_ => {
            calls++;
            throw new UpstreamHttpException("denied", HttpStatusCode.Forbidden, null);
        }, concurrency: 1);
        var error = await Assert.ThrowsAsync<PartialScrapeException>(() =>
            service.GetAllOffersAsync(14, 4, 8959, 500_000, 799_999));
        Assert.Empty(error.Offers);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task KeepsFirstPageWhenLaterPageExhaustsRetries()
    {
        int calls = 0;
        var service = Create(url => {
            calls++;
            if (url.Contains("offset=0&")) return Task.FromResult(Page(1, 40));
            throw new HttpRequestException("network");
        });
        var error = await Assert.ThrowsAsync<PartialScrapeException>(() =>
            service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
        Assert.Equal(40, error.Offers.Count);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ContinuesAfterAnEntirePageIsExcludedByTitleFilter()
    {
        var service = Create(url => Task.FromResult(url.Contains("offset=0&")
            ? Page(1, 40, "TBS apartment") : Page(41, 1)));
        var offers = await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999);
        Assert.Equal("41", Assert.Single(offers).Id);
        Assert.Equal(500000, offers[0].Price);
    }

    [Fact]
    public async Task StopsRepeatedPagesAndDeduplicatesPromotions()
    {
        int calls = 0;
        var service = Create(_ => { calls++; return Task.FromResult(Page(1, 46)); });
        var offers = await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999);
        Assert.Equal(46, offers.Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UsesVisibleTotalWhenPromotedOffersMakeTheLastPageLookFull()
    {
        int calls = 0;
        var service = Create(_ => { calls++; return Task.FromResult(Page(1, 46, total: 40)); });
        Assert.Equal(46, (await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999)).Count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RejectsAnErrorObjectInsteadOfTreatingItAsAnEmptySearch()
    {
        var service = Create(_ => Task.FromResult(Json(new { error = "unavailable" })));
        await Assert.ThrowsAsync<PartialScrapeException>(() =>
            service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
    }

    [Fact]
    public async Task ToleratesNullRecordsAndParameters()
    {
        var service = Create(_ => Task.FromResult(Json(new {
            data = new object?[] { null, new { id = "1", url = "https://www.olx.pl/d/1", @params = (object?)null } }
        })));
        Assert.Single(await service.GetAllOffersAsync(14, 4, 8959, 500_000, 549_999));
    }

    [Fact]
    public async Task UnsupportedCityMakesNoRequests()
    {
        var service = Create(_ => throw new Exception("Must not be called"));
        Assert.Empty((await service.GetOLXResponse(CityEnum.Tychy)).Data);
    }
}
