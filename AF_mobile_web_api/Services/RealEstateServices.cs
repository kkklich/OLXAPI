using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Helper;
using Newtonsoft.Json;


namespace AF_mobile_web_api.Services
{
    public class RealEstateServices
    {
        private readonly HTTPClientServices _httpClient;
       
        public RealEstateServices(HTTPClientServices httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<QueryData> GetDefaultResponse(int offset = 0, int limit = 40, int priceFrom = 50000, int priceTo = 250000)
        {
            int categoryId = ConstantHelper.RealEstateCategory;
            int regionId = ConstantHelper.LesserPolandRegionId;
            int cityId = ConstantHelper.KrakowCityId;

            var realEstateQuery = ConstantHelper.OLXAPI;

            var queryParams = $"?offset={offset}" +
                $"&limit={limit}" +
                $"&category_id={categoryId}" +
                $"&region_id={regionId}" +
                $"&city_id={cityId}" +
                $"&filter_float_price%3Afrom={priceFrom}" +
                $"&filter_float_price%3Ato={priceTo}";

            realEstateQuery += queryParams;

            var rawResponse = await _httpClient.GetRaw(realEstateQuery);
            var result = await rawResponse.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<QueryData>(result);

            return data;
        }

        public async Task<MarketplaceSearch> GetMoreResponse()
        {
            MarketplaceSearch searcheddata = new MarketplaceSearch();
            int limit = 40;
            for (int j = 0; j < 4; j++)//4
            {
                for (int i = 0; i < 25; i++)//25
                {
                    var response = await GetDefaultResponse(i * limit, limit, (200000 * j) + 50000, (200000 * j) + 250000);                    
                    var result = ExtractListOfParameters(response.Data);
                    searcheddata.Data.AddRange(result);
                }
            }

            searcheddata.TotalCount = searcheddata.Data.Count;
         
            return searcheddata;
        }

        private List<SearchData> ExtractListOfParameters(List<Data> responses)
        {
            List<SearchData> result = new List<SearchData>();
            foreach (var response in responses)
            {
                var extractedObject = ExtractParameter(response);
                result.Add(extractedObject);
            }

            return result;            
        }

        private SearchData ExtractParameter(Data data)
        {
            var record = MapParamsToSearchData(data.Params);

            record.Id = data.Id;
            record.Url = data.Url;
            record.Title = data.Title;
            record.CreatedTime = data.created_time;
            record.Private = !data.Business;
            record.Description = data.Description;
            record.Location = new LocationPlace() 
            {
                City = data?.Location?.City?.Name ?? string.Empty,
                District = data?.Location?.District?.Name ?? string.Empty,
                Lat = data?.Map?.Lat ??  0,
                Lon = data?.Map?.Lon ?? 0
            };

            record.Photos.AddRange(data.Photos.Select(p => new Photos
            {
                Id = p.Id,
                Filename = p.Filename,
                Width = p.Width,
                Height = p.Height,
                Link = p.Link
            }));


            return record;
        }

        public SearchData MapParamsToSearchData(List<Param> parameters)
        {
            var data = new SearchData();
            foreach (var param in parameters)
            {
                switch (param.Key)
                {
                    case ConstantHelper.Price:
                        if (param.Value?.Label != null)
                            data.Price = ParsePriceToDouble(param.Value?.Label);
                        break;
                    case ConstantHelper.PricePerMeter:
                        if (double.TryParse(param.Value?.Key, out var pricePerM))
                            data.PricePerMeter = pricePerM;
                        break;
                    case ConstantHelper.FloorSelect:
                        if (int.TryParse(param.Value?.Label, out var floor))
                            data.Floor = floor;
                        break;
                    case ConstantHelper.Market:
                        data.Market = param.Value?.Label ?? string.Empty;
                        break;

                    case ConstantHelper.BuildType:
                        data.BuildingType = param.Value?.Label;
                        break;

                    case ConstantHelper.Area:
                        if (double.TryParse(param.Value?.Key, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var area))
                        {
                            data.Area = area;
                        }
                        break;
                }
            }

            return data;
        }


        private double ParsePriceToDouble(string priceString)
        {
            if (string.IsNullOrWhiteSpace(priceString))
                return 0;

            var cleaned = priceString.Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(" ", "")
                                     .Trim();

            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return 0;
        }
    }
}
