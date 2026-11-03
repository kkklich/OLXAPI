using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Services;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class PropertyUrlFilterTests
{
    private static OfferSnapshot Offer(string url, string city = "Krakow", double price = 500000, int website = 0) =>
        new OfferSnapshotBuilder().Build(new LatestOfferRow {
            Id = Guid.NewGuid(), Url = url, City = city, Price = price, WebName = website,
            Title = "Apartment", LastSeen = DateTime.UtcNow
        });

    [Theory]
    [InlineData("https://www.olx.pl/d/oferta/loft-IDABC.html")]
    [InlineData("  HTTPS://WWW.OLX.PL/D/OFERTA/LOFT-idabc.HTML  ")]
    [InlineData("idabc")]
    public void FindsFullOrPartialUrlIgnoringCaseAndSurroundingWhitespace(string filter)
    {
        var wanted = Offer("https://www.olx.pl/d/oferta/loft-IDABC.html");
        var other = Offer("https://www.morizon.pl/oferta/other");
        var page = OfferQuery.Apply(new[] { other, wanted }, new PropertyQueryParams { Url = filter });
        Assert.Same(wanted, Assert.Single(page.Items));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public void FiltersBeforeCountingAndPagingAndCombinesWithExistingFilters()
    {
        var offers = new[] {
            Offer("https://www.olx.pl/match/other-city", "Katowice"),
            Offer("https://www.olx.pl/match/too-expensive", price: 800000),
            Offer("https://www.olx.pl/match/other-website", website: 1),
            Offer("https://www.olx.pl/unrelated"),
            Offer("https://www.olx.pl/match/first"),
            Offer("https://www.olx.pl/match/second")
        };
        var page = OfferQuery.Apply(offers, new PropertyQueryParams {
            Url = "/match/", City = "Krakow", WebName = 0, PriceMax = 600000, Page = 2, PageSize = 1
        });
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Page);
        Assert.StartsWith("https://www.olx.pl/match/", page.Items[0].Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyUrlLeavesOtherFiltersUnchanged(string? filter)
    {
        var offers = new[] { Offer("https://www.olx.pl/one"), Offer("https://www.olx.pl/two", "Katowice") };
        Assert.Single(OfferQuery.Apply(offers, new PropertyQueryParams { Url = filter, City = "Krakow" }).Items);
    }

    [Theory]
    [InlineData("100%_done?ref=search&tag=a+b#photos", 1)]
    [InlineData("%", 1)]
    [InlineData("absent", 0)]
    public void MatchesUrlCharactersLiterally(string filter, int expected)
    {
        var offers = new[] {
            Offer("https://www.olx.pl/100%_done?ref=search&tag=a+b#photos"),
            Offer("https://www.olx.pl/100-other-done")
        };
        Assert.Equal(expected, OfferQuery.Apply(offers, new PropertyQueryParams { Url = filter }).TotalCount);
    }
}
