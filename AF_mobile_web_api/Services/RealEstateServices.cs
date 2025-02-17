using System.Globalization;
using System.Text.RegularExpressions;
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
            int categoryId = 14;
            int regionId = 4;
            int cityId = 8959;

            var realEstateQuery = ConstantHelper.OLXAPI;

            var queryParams = $"?offset={offset}" +
                $"&limit={limit}" +
                $"&category_id={categoryId}" +
                $"&region_id={regionId}" +
                $"&city_id={cityId}" +
                $"&filter_float_price%3Afrom={priceFrom}" +
                $"&filter_float_price%3Ato={priceTo}";



            //var queryParams = $"?offset={offset}" +
            //    $"&limit={limit}" +
            //    $"&category_id={ConstantHelper.RealEstateCategory}" +
            //    $"&region_id={4}" +
            //    $"&city_id={8959}";
            realEstateQuery += queryParams;

            var rawResponse = await _httpClient.GetRaw(realEstateQuery);

            var result = await rawResponse.Content.ReadAsStringAsync();

            var data = JsonConvert.DeserializeObject<QueryData>(result);


            return data;
        }

        public async Task<SearchResults> GetMoreResponse()
        {
            SearchResults results = new SearchResults();
            int limit = 40;
            int totalQuantityResult = 0;
            int A = 100000;
            int B = 800000;

            int step = 200000;
            for (int j = 0; j < (B - A) / step; j++)
            {
                for (int i = 0; i < 25; i++)
                {
                    int startRange = A + (step * j);
                    int endRange = startRange + step;
                    var response = await GetDefaultResponse(i * limit, limit, startRange, endRange);


                    await Task.Delay(20);

                    var extractedData = ExtractListOfParameters(response.Data);
                    results.Data.AddRange(extractedData);

                    if (response.metadata.visible_total_count > totalQuantityResult)
                        totalQuantityResult = response.metadata.visible_total_count;

                }
            }

            //100 000 -> 900 000
            //for (int j = 0; j < 4; j++)//4
            //{
            //    for (int i = 0; i < 25; i++)//25
            //    {
            //        var response = await GetDefaultResponse(i * limit, limit, (200000 * j) + 50000, (200000 * j) + 250000);

            //        await Task.Delay(20);

            //        var extractedData =  ExtractListOfParameters(response.Data);
            //        results.Data.AddRange(extractedData);

            //        if(response.metadata.visible_total_count > totalQuantityResult)
            //            totalQuantityResult = response.metadata.visible_total_count;
            //    }
            //}

            results.Data = results.Data.GroupBy(o => o.Id).Select(g => g.First()).OrderBy(x => x.created_time).ToList();
            results.Total_elements = totalQuantityResult;
         
            return results;
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
                    //case "furniture":
                    //    extractedResult.Furniture = param.Value.Key;
                    //    break;
                    //case "market":
                    //    extractedResult.Market = param.Value.Key;
                    //    break;
                    case "price":
                        //if(param.Value.Key)
                        var priceSelected = param.Value.Key != null ? param.Value.Key : param.Value.Label;
                        extractedResult.Price = ExtractNumberFromString(priceSelected);
                        break;
                    //case "builttype":
                    //    extractedResult.Builttype = param.Value.Key;
                    //    break;
                    case "m":
                        extractedResult.Area = ExtractNumberFromString(param?.Value?.Key ?? "");
                        break;
                    //case "rooms":
                    //    extractedResult.Rooms = param.Value.Key;
                    //    break;
                    default:
                        break;
                }                                                                                                                                                                                                                                      
                
            }

            return extractedResult;
        }

        public static double ExtractNumberFromString(string input)
        {
            //Match match = Regex.Match(input, @"\d+(\.\d+)?");
            //if (match.Success)
            //{
            //    if (double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
            //    {
            //        return number;
            //    }
            //}

            Match match = Regex.Match(input, @"\d+([\s.,]?\d+)*");
            if (match.Success)
            {
                // Remove spaces and convert to double
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
