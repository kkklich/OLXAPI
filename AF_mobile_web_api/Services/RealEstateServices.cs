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
        private readonly AppDbContext _dbContext;
        private readonly OLXAPIService _olxApiService;
        private readonly MorizonApiService _morizonApiService;
        private readonly NieruchomosciOnlineService _nieruchomosciOnlineService;
            
        public RealEstateServices(HTTPClientServices httpClient, AppDbContext dbContext, OLXAPIService olxApiService, MorizonApiService morizonApiService, NieruchomosciOnlineService nieruchomosciOnlineService)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
        }        

        public async Task<MarketplaceSearch> GetDataSave()
        {
            var recentEntry = await _dbContext.WebSearchResults
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
            var responseNieruchomosci = await _nieruchomosciOnlineService.GetAllPagesAsync();
            var responseMorizon = await _morizonApiService.GetPropertyListingDataAsync();
            var responseOLX = await _olxApiService.GetOLXResponse();          


            MarketplaceSearch combinedData = new MarketplaceSearch();
            combinedData.Data = responseOLX.Data.Union(responseMorizon.Data).ToList();
            combinedData.Data = combinedData.Data.Union(responseNieruchomosci.Data).ToList();

            combinedData.TotalCount = responseOLX.TotalCount + responseMorizon.TotalCount + responseNieruchomosci.TotalCount;

            var combinedResponse = new MarketplaceSearch
            {
                TotalCount = combinedData.TotalCount,
                Data = combinedData.Data
            };

            WebSearchResults findings = new WebSearchResults()
            {
                Name = combinedData.TotalCount.ToString(),
                Content = JsonConvert.SerializeObject(combinedResponse.Data),
                CreationDate = DateTime.UtcNow
            };

            _dbContext.WebSearchResults.Add(findings);
            await _dbContext.SaveChangesAsync();
            SavePropertyDataToDatabase(combinedData.Data);

            return combinedData;
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

            _dbContext.PropertyData.AddRange(propertiesList);
            _dbContext.SaveChanges();
        }
    }
}
