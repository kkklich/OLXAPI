using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Mappings;
using ApplicationDatabase.Models;
using System.Text.Json;
using Xunit;

namespace AF_mobile_web_api.Tests;

public class PropertyMappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMappingsPreserveExistingValues(bool missingLocation)
    {
        var source = new SearchData
        {
            Id = "portal-id",
            Url = "https://example.com/oferta/ą?b=2&a=1",
            Title = "Mieszkanie – Łódź",
            Price = 645795.50,
            PricePerMeter = 12345.67,
            Floor = -1,
            Market = "Wtorny",
            BuildingType = "Kamienica",
            Area = 52.31,
            Private = true,
            WebName = (WebName)99,
            Location = missingLocation ? null! : new LocationPlace
            {
                City = "Krakow", District = "Centrum", Lat = 50.06143, Lon = 19.93658
            }
        };

        var row = PropertyMapping.ToPropertyData(source);
        // Verified against the former AutoMapper profile before dependency removal.
        var expectedRow = new PropertyData
        {
            Url = "https://example.com/oferta/ą?b=2&a=1", Title = "Mieszkanie – Łódź",
            Price = 645795.50, PricePerMeter = 12345.67, Floor = -1,
            Market = "Wtorny", BuildingType = "Kamienica", Area = 52.31, Private = true,
            WebName = 99, City = missingLocation ? null! : "Krakow",
            District = missingLocation ? null! : "Centrum",
            Lat = missingLocation ? 0 : 50.06143, Lon = missingLocation ? 0 : 19.93658
        };
        var expectedDto = new SearchDataDTO
        {
            Id = "portal-id", Url = "https://example.com/oferta/ą?b=2&a=1", Title = "Mieszkanie – Łódź",
            Price = 645795.50, PricePerMeter = 12345.67, Floor = -1,
            Market = "Wtorny", BuildingType = "Kamienica", Area = 52.31, Private = true,
            WebName = (WebName)99
        };
        Assert.Equal(JsonSerializer.Serialize(expectedRow), JsonSerializer.Serialize(row));
        Assert.Equal(JsonSerializer.Serialize(expectedDto), JsonSerializer.Serialize(PropertyMapping.ToDto(source)));
        row.Id = Guid.Parse("837575fd-9445-452b-8e23-273e082dc94b");
        source.Id = row.Id.ToString();
        source.Location ??= new LocationPlace();
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(PropertyMapping.ToSearchData(row)));
    }

    [Fact]
    public void MappingLeavesDatabaseFieldsUnsetAndDoesNotMutateCachedSource()
    {
        var source = new SearchData { Id = "portal-id", Url = "https://example.com/offer", Location = null! };
        var row = PropertyMapping.ToPropertyData(source);
        Assert.Equal(Guid.Empty, row.Id);
        Assert.Equal(default, row.AddedRecordTime);
        Assert.Null(row.OffertId);
        Assert.Null(row.Description);
        Assert.Equal(default, row.CreatedTime);
        Assert.Null(row.City);
        Assert.Null(row.District);
        Assert.Equal(0, row.Lat);
        Assert.Equal(0, row.Lon);

        var dto = PropertyMapping.ToDto(source);
        dto.Url += ", https://example.com/duplicate";
        Assert.Equal("https://example.com/offer", source.Url);
        Assert.Equal(source.Url, PropertyMapping.ToDto(source).Url);

        var offer = PropertyMapping.ToSearchData(row);
        offer.Location.City = "Katowice";
        Assert.Null(row.City);
    }
}
