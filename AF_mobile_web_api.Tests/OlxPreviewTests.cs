using System.Net;
using AF_mobile_web_api.Controllers;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class OlxPreviewTests
{
    private static (OlxPreviewController Controller, Mock<IHTTPClientServices> Http) Create(string json)
    {
        var http = new Mock<IHTTPClientServices>(MockBehavior.Strict);
        http.Setup(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var service = new OLXAPIService(http.Object, NullLogger<OLXAPIService>.Instance,
            Options.Create(new OlxScrapeOptions { MaxAttempts = 1 }));
        // The preview can execute using only the HTTP downloader: no database or
        // persistence services are constructed, even on success and error paths.
        var controller = new OlxPreviewController(service, NullLogger<OlxPreviewController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, http);
    }

    [Fact]
    public async Task ReturnsOneMappedPageWithoutFollowingPaginationOrSplittingPrices()
    {
        var (controller, http) = Create("""
            {"data":[{"id":123,"url":"https://www.olx.pl/d/oferta/123","title":"Mieszkanie",
              "params":[{"key":"m","value":{"key":"25.55"}},
                        {"key":"price","value":{"label":"420 405 zł"}}],
              "location":{"city":{"name":"Kraków"},"district":{"name":"Prądnik Biały"}},
              "map":{"lat":50.094366,"lon":19.925657}}],
             "metadata":{"total_elements":1000,"visible_total_count":2427}}
            """);

        var response = Assert.IsType<OkObjectResult>(await controller.Get(limit: 2));
        var preview = Assert.IsType<OlxPreviewResponse>(response.Value);
        Assert.False(preview.SavedToDatabase);
        Assert.Equal(2427, preview.TotalCount);
        Assert.Equal(1, preview.ReturnedCount);
        var offer = Assert.Single(preview.Data);
        Assert.Equal("123", offer.Id);
        Assert.Equal(420405, offer.Price);
        Assert.Equal(25.55, offer.Area);
        Assert.Equal(420405 / 25.55, offer.PricePerMeter, 2);
        Assert.Equal("Kraków", offer.Location.City);
        Assert.Equal(50.094366, offer.Location.Lat);
        http.Verify(x => x.GetRaw(preview.SourceUrl, It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()), Times.Once);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForwardsCityPaginationAndPriceFilters()
    {
        var (controller, http) = Create("""{"data":[],"metadata":{"visible_total_count":0}}""");
        var response = Assert.IsType<OkObjectResult>(await controller.Get("katowice", 100000, 700000, 40, 20));
        var preview = Assert.IsType<OlxPreviewResponse>(response.Value);
        Assert.Equal("Katowice", preview.City);
        Assert.Equal(40, preview.Offset);
        Assert.Equal(20, preview.Limit);
        Assert.Equal("https://www.olx.pl/api/v1/offers/?offset=40&limit=20&category_id=14&filter_float_price%3Afrom=100000&filter_float_price%3Ato=700000&region_id=6&city_id=7691", preview.SourceUrl);
        Assert.Empty(preview.Data);
        http.Verify(x => x.GetRaw(preview.SourceUrl, It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Theory]
    [InlineData("unknown", 0, 500000, 0, 40)]
    [InlineData("1", 0, 500000, 0, 40)]
    [InlineData("Tychy", 0, 500000, 0, 40)]
    [InlineData("Krakow", -1, 500000, 0, 40)]
    [InlineData("Krakow", 500000, 499999, 0, 40)]
    [InlineData("Krakow", 0, 500000, 0, 0)]
    [InlineData("Krakow", 0, 500000, 0, 41)]
    [InlineData("Krakow", 0, 500000, -1, 40)]
    [InlineData("Krakow", 0, 500000, 999, 40)]
    [InlineData("Krakow", 0, 500000, int.MaxValue, 40)]
    public async Task InvalidInputReturns400WithoutContactingOlx(string city, int from, int to, int offset, int limit)
    {
        var (controller, http) = Create("{}");
        Assert.IsType<BadRequestObjectResult>(await controller.Get(city, from, to, offset, limit));
        http.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{\"error\":\"unavailable\"}")]
    [InlineData("<html>not JSON</html>")]
    public async Task InvalidOlxResponseReturns502InsteadOfAnEmptySuccess(string body)
    {
        var (controller, _) = Create(body);
        var response = Assert.IsType<ObjectResult>(await controller.Get());
        Assert.Equal(502, response.StatusCode);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task OlxHttpFailureReturns502(int status)
    {
        var (controller, http) = Create("{}");
        http.Setup(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()))
            .ThrowsAsync(new HttpRequestException("upstream failure", null, (HttpStatusCode)status));
        var response = Assert.IsType<ObjectResult>(await controller.Get());
        Assert.Equal(502, response.StatusCode);
    }

    [Fact]
    public async Task OlxTimeoutReturns504()
    {
        var (controller, http) = Create("{}");
        http.Setup(x => x.GetRaw(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<IDictionary<string, string>>()))
            .ThrowsAsync(new TaskCanceledException("timeout"));
        var response = Assert.IsType<ObjectResult>(await controller.Get());
        Assert.Equal(504, response.StatusCode);
    }
}
