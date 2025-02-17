using System.Globalization;
using System.Text.RegularExpressions;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Helper;
using Microsoft.AspNetCore.Mvc;
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
            int categoryId = 14; //sprzedaz mieszkan
            int regionId = 4; //krakow
            int cityId = 8959;  //Krakow

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

        public async Task<SearchResults> GetMoreResponse(int? floorSelect, int? minPrice, int? maxPrice, int? minArea, int? maxArea)
        {
            SearchResults results = new SearchResults();
            int limit = 40;
            int totalQuantityResult = 0;
            int A = minPrice ?? 150000;
            int B = maxPrice ?? 1000000;

            int step = 200000;
            for (int j = 0; j < (B - A) / step; j++)
            {
                for (int i = 0; i < 25; i++)//25
                {
                    int startRange = A + (step * j);
                    int endRange = startRange + step;
                    var response = await GetDefaultResponse(i * limit, limit, startRange, endRange);

                    await Task.Delay(2);

                    var extractedData = ExtractListOfParameters(response.Data);
                    results.Data.AddRange(extractedData);

                    if (response.metadata.visible_total_count > totalQuantityResult)
                        totalQuantityResult = response.metadata.visible_total_count;

                }
            }

            results.Data = results.Data.GroupBy(o => o.Id).Select(g => g.First()).ToList();
            results.Total_elements = totalQuantityResult;

            results.Data = FilterResult(results.Data, floorSelect, minPrice, maxPrice, minArea, maxArea);           

            return results;
        }

        private List<DataSearch> FilterResult(List<DataSearch> datas, int? floorSelect, int? minPrice, int? maxPrice, int? minArea, int? maxArea)
        {
            if (floorSelect != null)
                datas = datas.Where(x => x.FloorSelect == floorSelect).ToList();

            if (minPrice != null)
                datas = datas.Where(x => x.Price > minPrice).ToList();

            if (maxPrice != null)
                datas = datas.Where(x => x.Price < maxPrice).ToList();

            if(minArea != null) 
                datas = datas.Where(x => x.Area > minArea).ToList();
            
            if(maxArea != null) 
                datas = datas.Where(x => x.Area < maxArea).ToList();

            datas = datas.OrderBy(y => y.PricePerM).ToList();

            return datas;
        }

        private List<DataSearch> ExtractListOfParameters(List<Data> responses)
        {
            List<DataSearch> results = new List<DataSearch>();
            foreach (var response in responses)
            {              
                var resultWithParameters = ExtractParameter(response.Params);

                resultWithParameters.Title = response.Title;
                resultWithParameters.created_time = response.created_time;
                resultWithParameters.Id = response.Id;
                resultWithParameters.Url = response.Url;

                results.Add(resultWithParameters);
            }

            return results;
        }


        private DataSearch ExtractParameter(List<Param> parameters)
        {
            DataSearch extractedResult = new DataSearch();
            foreach (var param in parameters)
            {
                switch (param.Key)
                {
                    case "price_per_m":
                        extractedResult.PricePerM = ExtractNumberFromString(param?.Value?.Key ?? "");
                        break;
                    case "floor_select":
                        extractedResult.FloorSelect = ExtractNumberFromString(param?.Value?.Key ?? "");
                        break;                 
                    case "price":
                        var priceSelected = param.Value.Key != null ? param.Value.Key : param.Value.Label;
                        extractedResult.Price = ExtractNumberFromString(priceSelected);
                        break;              
                    case "m":
                        extractedResult.Area = ExtractNumberFromString(param?.Value?.Key ?? "");
                        break;
                    default:
                        break;
                }  
            }

            return extractedResult;
        }

        public static double ExtractNumberFromString(string input)
        {
            Match match = Regex.Match(input, @"\d+([\s.,]?\d+)*");
            if (match.Success)
            {
                string numberString = match.Value.Replace(" ", "").Replace(",", ".");
                if (double.TryParse(numberString, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
                {
                    return number;
                }
            }

            return 0;
        }
    }
}
