using System.Globalization;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Helper;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;


namespace AF_mobile_web_api.Services
{
    public class RealEstateServices
    {
        private readonly HTTPClientServices _httpClient;
        private readonly AppDbContext _context;

        public RealEstateServices(HTTPClientServices httpClient, AppDbContext context)
        {
            _httpClient = httpClient;
            _context = context;
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

        public async Task<MarketplaceSearch> GetDataSave()
        {
            var recentEntry = await _context.WebSearchResults
                .Where(w => w.CreationDate >= DateTime.UtcNow.AddDays(-5))
                .OrderByDescending(w => w.CreationDate)
                .FirstOrDefaultAsync();

            if (recentEntry != null)
            {
                var deserializedData = JsonConvert.DeserializeObject<List<SearchData>>(recentEntry.Content);
                MarketplaceSearch result = new MarketplaceSearch()
                {
                    Data = deserializedData ?? new List<SearchData>(),
                    TotalCount = recentEntry.Name != null ? int.Parse(recentEntry.Name) + 1000000 : 0,
                };
                return result;
            }

            // Proceed with fetching and saving data
            var response = await GetMoreResponse();
            WebSearchResults findings = new WebSearchResults()
            {
                Name = response.TotalCount.ToString(),
                Content = JsonConvert.SerializeObject(response.Data),
                CreationDate = DateTime.UtcNow
            };

            _context.WebSearchResults.Add(findings);
            await _context.SaveChangesAsync();
            SavePropertyDataToDatabase(response.Data);

            return response;
        }

        private void SavePropertyDataToDatabase(List<SearchData> data)
        {
            List<PropertyData> propertiesList = new List<PropertyData>();
            foreach (var item in data)
            {               
                var property = new PropertyData
                {
                    OffertId = item.Id,
                    Url = item.Url,
                    Title = item.Title,
                    CreatedTime = item.CreatedTime,
                    Private = item.Private,
                    Price = item.Price,
                    PricePerMeter = item.PricePerMeter,
                    Floor = item.Floor,
                    Market = item.Market,
                    BuildingType = item.BuildingType,
                    Area = item.Area,              
                    City = item.Location.City,
                    AddedRecordTime = DateTime.UtcNow
                };

                propertiesList.Add(property);
            }

            _context.PropertyData.AddRange(propertiesList);
            _context.SaveChanges();
        }
        
        public async Task<MarketplaceSearch> GetMoreResponse()
        {
            MarketplaceSearch searchedData = new MarketplaceSearch();
            int limit = 40;

            var allTasks = new List<Task<List<SearchData>>>();

            for (int j = 0; j < 3; j++)//5
            {
                for (int i = 0; i < 25; i++)
                {
                    int offset = i * limit;
                    int minPrice = (200000 * j) + 50000;
                    int maxPrice = (200000 * j) + 250000;

                    // Launch the task
                    allTasks.Add(Task.Run(async () =>
                    {
                        var response = await GetDefaultResponse(offset, limit, minPrice, maxPrice);
                        return ExtractListOfParameters(response.Data);
                    }));
                }
            }

            var results = await Task.WhenAll(allTasks);

            foreach (var result in results)
            {
                searchedData.Data.AddRange(result);
            }

            searchedData.Data = searchedData.Data
                .DistinctBy(x => x.Id)
                .OrderBy(x => x.PricePerMeter)
                .ToList();

            searchedData.TotalCount = searchedData.Data.Count;

            return searchedData;
        }


        private List<SearchData> ExtractListOfParameters(List<Data> responses)
        {
            List<SearchData> result = new List<SearchData>();
            foreach (var response in responses)
            {
                if(response.Title.Contains("TBS"))
                    continue;
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

            //record.Photos.AddRange(data.Photos.Select(p => new Photos
            //{
            //    Id = p.Id,
            //    Filename = p.Filename,
            //    Width = p.Width,
            //    Height = p.Height,
            //    Link = p.Link
            //}));


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
                            data.Price = ParseNumberToDouble(param.Value?.Label);
                        break;
                    case ConstantHelper.PricePerMeter:                      
                        data.PricePerMeter = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);
                         
                        if (data.PricePerMeter == 0)
                            data.PricePerMeter = data.Price / data.Area;
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
                        data.Area = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);
                        break;
                }
            }

            return data;
        }


        private double ParseNumberToDouble(string priceString)
        {
            if (string.IsNullOrWhiteSpace(priceString))
                return 0;

            var cleaned = priceString.Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(" ", "")
                                     .Replace(",", ".", StringComparison.OrdinalIgnoreCase)
                                     .Trim();

            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return 0;
        }
    }
}
