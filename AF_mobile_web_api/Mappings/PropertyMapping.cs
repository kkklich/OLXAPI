using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Mappings;

// These fixed shapes need only scalar copies. Keep database-generated fields and
// the scrape timestamp untouched; the write path assigns them separately.
public static class PropertyMapping
{
    public static PropertyData ToPropertyData(SearchData source) => new()
    {
        Url = source.Url,
        Title = source.Title,
        Price = source.Price,
        PricePerMeter = source.PricePerMeter,
        Floor = source.Floor,
        Market = source.Market,
        BuildingType = source.BuildingType,
        Area = source.Area,
        Private = source.Private,
        WebName = (int)source.WebName,
        City = source.Location?.City!,
        District = source.Location?.District!,
        Lat = source.Location?.Lat ?? 0,
        Lon = source.Location?.Lon ?? 0
    };

    public static SearchData ToSearchData(PropertyData source) => new()
    {
        Id = source.Id.ToString(),
        Url = source.Url,
        Title = source.Title,
        Price = source.Price,
        PricePerMeter = source.PricePerMeter,
        Floor = source.Floor,
        Market = source.Market,
        BuildingType = source.BuildingType,
        Area = source.Area,
        Private = source.Private,
        WebName = (WebName)source.WebName,
        Location = new LocationPlace
        {
            City = source.City,
            District = source.District,
            Lat = source.Lat,
            Lon = source.Lon
        }
    };

    // A fresh DTO is essential: duplicate-offer aggregation appends URLs here,
    // while the source is shared by every reader of the cached latest batch.
    public static SearchDataDTO ToDto(SearchData source) => new()
    {
        Id = source.Id,
        Url = source.Url,
        Title = source.Title,
        Price = source.Price,
        PricePerMeter = source.PricePerMeter,
        Floor = source.Floor,
        Market = source.Market,
        BuildingType = source.BuildingType,
        Area = source.Area,
        Private = source.Private,
        WebName = source.WebName
    };
}
