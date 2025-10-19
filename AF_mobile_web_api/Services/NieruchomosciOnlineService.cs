using AF_mobile_web_api.DTO;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AF_mobile_web_api.Services
{
    public class NieruchomosciOnlineService
    {
        private readonly HTTPClientServices _httpClient;

        public NieruchomosciOnlineService(HTTPClientServices httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<MarketplaceSearch> GetApartmentListingsAsync()
        {
            string url = "https://www.nieruchomosci-online.pl/szukaj.html?3,mieszkanie,sprzedaz,,Krak%C3%B3w,,,,-565000&ajax=1";

            var headers = new Dictionary<string, string>
            {
                ["X-Requested-With"] = "XMLHttpRequest"
            };

            try
            {
                var response = await _httpClient.GetRaw(url, null, headers);
                // Ensure the request was successful
                response.EnsureSuccessStatusCode();
                // Read the response content as a string (likely JSON)
                string responseBody = await response.Content.ReadAsStringAsync();

                var itemsAdditional = ParseListAdditionalData(responseBody);
                var itemsProps  = ParseListRecordPropsData(responseBody);

                var result = Convert(itemsAdditional, itemsProps);

                return result;
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Request Error: {e.Message}");
                return null; // Or handle the error appropriately
            }
        }


        private readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private int? GetFloor(JToken attributesToken)
        {
            if (attributesToken == null || attributesToken.Type != JTokenType.Array)
                return null;

            var floorAttr = attributesToken
                .FirstOrDefault(a =>
                    string.Equals((string)a["alias"], "floor", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string)a["label"], "Piętro", StringComparison.OrdinalIgnoreCase));
            if (floorAttr == null)
                return null;

            var values = floorAttr["values"] as JArray;
            if (values == null)
                return null;

            // Pick the first numeric value; the array often looks like ["1","/","2"], where first is current floor.
            foreach (var v in values)
            {
                var valStr = (string)v["value"];
                if (int.TryParse(valStr, out var num))
                    return num;
            }
            return null;
        }

        private List<AdditionalData> ParseListAdditionalData(string json)
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("listAdditionalData", out var lad) ||
                lad.ValueKind != JsonValueKind.Object)
                return new List<AdditionalData>();

            var result = new List<AdditionalData>(lad.GetRawText().Length / 5000 + 1); // rough prealloc

            foreach (var prop in lad.EnumerateObject())
            {
                var data = prop.Value.Deserialize<AdditionalData>(Options)
                           ?? new AdditionalData();

                data.Id = prop.Name;
                var floor = data.Attributes?.FirstOrDefault(x => x.Alias == "floor")?.Values?.FirstOrDefault(x => int.TryParse(x.Value, out var i))?.Value;
                data.Floor = floor != null ? int.Parse(floor) : -1;

                result.Add(data);
            }

            return result;
        }

        //record_props
        private List<RecordProps> ParseListRecordPropsData(string json)
        {
            json = json.Replace("record_props", "recordprops");
            using var doc = JsonDocument.Parse(json);

            if (!(doc.RootElement.TryGetProperty("pb", out var pbElement) &&  pbElement.TryGetProperty("recordprops", out var lad)))
                return new List<RecordProps>();
            
            var result = new List<RecordProps>(lad.GetRawText().Length / 5000 + 1); // rough prealloc

            foreach (var prop in lad.EnumerateObject())
            {
                var data = prop.Value.Deserialize<RecordProps>(Options)
                           ?? new RecordProps();
                data.Id = prop.Name;

                result.Add(data);
            }

            return result;
        }

        private MarketplaceSearch Convert(List<AdditionalData> items, List<RecordProps> recordProps)
        {
            var result = new MarketplaceSearch();

            long runningId = 1;
            foreach (var a in items)
            {
                var b = recordProps.FirstOrDefault(x => x.Id == a.Id);
                var sd = MapOne(a, b, runningId++);
                result.Data.Add(sd);
            }

            result.TotalCount = result.Data.Count;
            return result;
        }

        private SearchData MapOne(AdditionalData a, RecordProps b, long internalId)
        {
            var area = b?.Rsur ?? 1.0D;
            var price = ParseDouble(a.PrimaryPrice);
            var buildingType = b.Rccata?.ToLower().Contains("flats") == true ? "Mieszkania" : b.Rccata ?? "";
            var sd = new SearchData
            {
                Id = internalId, //TODO chanege to string
                WebName = WebName.NieruchomsciOnline,
                Url = a.ShareUrl ?? "",
                Title = a.MetaTitle ?? "",
                CreatedTime = ParsePolishDate(a.ChangeDate) ?? ParsePolishDate(a.ModDate) ?? DateTime.MinValue,
                Price = price,
                Market = a.Market switch
                {
                    "primary" => "pierwotny",
                    "secondary" => "wtorny",    
                    _ => a.Market ?? "pierwotny"
                },
                Private = !b.Pbh.ToLower().Contains("agency") && !b.Pbh.ToLower().Contains("dev"),
                Area = area, 
                PricePerMeter =  price / area,
                Location = new LocationPlace
                {
                    Lat = ParseDouble(a.Map?.Latitude),
                    Lon = ParseDouble(a.Map?.Longitude),
                    City = a?.DlData?.CityName ?? "",
                    District = a?.DlData?.QuarterName ?? "",
                    Street = b?.Rlocstsn ?? "",
                    Number = ""
                },
                Description = a?.MetaTitle ?? "",
                Floor = a.Floor,                
                Photos = new List<Photos>(), //todo get from photos json,
                BuildingType = buildingType
            };
            
            return sd;
        }

        private double ParseDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(" ", "").Replace("\u00A0", "");
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }       

        private DateTime? ParsePolishDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var formats = new[] { "d MMMM yyyy", "dd MMMM yyyy", "d MMM yyyy", "dd MMM yyyy" };
            if (DateTime.TryParseExact(s, formats, new CultureInfo("pl-PL"),
                    DateTimeStyles.AssumeLocal, out var dt))
                return dt;

            return DateTime.TryParse(s, new CultureInfo("pl-PL"),
                DateTimeStyles.AssumeLocal, out dt) ? dt : null;
        }
    }
}
