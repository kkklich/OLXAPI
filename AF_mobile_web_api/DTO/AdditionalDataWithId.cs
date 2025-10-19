using System.Text.Json.Serialization;

namespace AF_mobile_web_api.DTO;


public class RecordProps
{
    public string Id { get; set; }           
    public string Rccata { get; set; }       // flats    -> building type
    public string Rlocstsn { get; set; }     // street (may be null)    -> street name 
    public string Pbh { get; set; }          // agency/dev/priv     -> isPrivate    
    public double? Rsur { get; set; }       // area      -> area
}

public sealed class AdditionalData
{
    public string Id { get; set; }
    public int Floor { get; set; }
    [JsonPropertyName("isSupplementAd")] public bool? IsSupplementAd { get; set; }
    [JsonPropertyName("map")] public MapData? Map { get; set; }
    // Attributes blocks
    [JsonPropertyName("attributes")] public List<AdAttribute>? Attributes { get; set; }

    // Contact
    [JsonPropertyName("contactBox")] public ContactBox? ContactBox { get; set; }

    // Dates and status
    [JsonPropertyName("modDate")] public string? ModDate { get; set; }
    [JsonPropertyName("changeDate")] public string? ChangeDate { get; set; }

    // Titles/headers
    [JsonPropertyName("metaTitle")] public string? MetaTitle { get; set; }
    [JsonPropertyName("shareUrl")] public string? ShareUrl { get; set; }
    
    // DL data summary
    [JsonPropertyName("dlData")] public DlData? DlData { get; set; }

    [JsonPropertyName("market")] public string? Market { get; set; }
    [JsonPropertyName("primaryPrice")] public string? PrimaryPrice { get; set; }
}

// Map and POIs
public sealed class MapData
{
    [JsonPropertyName("latitude")] public string? Latitude { get; set; }
    [JsonPropertyName("longitude")] public string? Longitude { get; set; }
    [JsonPropertyName("accuracy")] public string? Accuracy { get; set; }
}

// Attributes
public sealed class AdAttribute
{
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("values")] public List<AdAttributeValue>? Values { get; set; }
}

public sealed class AdAttributeValue
{
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

// Contact box
public sealed class ContactBox
{
    [JsonPropertyName("companyName")] public string? CompanyName { get; set; }
    [JsonPropertyName("phoneH")] public string? PhoneH { get; set; }
    // Dev/investment contact variant
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("developerName")] public string? DeveloperName { get; set; }
}

public sealed class DlData
{
    [JsonPropertyName("cityName")] public string? CityName { get; set; }
    [JsonPropertyName("regionName")] public string? RegionName { get; set; }
    [JsonPropertyName("quarterName")] public string? QuarterName { get; set; }
}