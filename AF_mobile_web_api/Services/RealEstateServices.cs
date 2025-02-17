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

        public async Task<QueryData> GetMoreResponse()
        {
            QueryData query = new QueryData();
            int limit = 40;
            int totalQuantityResult = 0;
            for (int j = 0; j < 1; j++)//4
            {
                for (int i = 0; i < 1; i++)//25
                {
                    var response = await GetDefaultResponse(i * limit, limit, (200000 * j) + 50000, (200000 * j) + 250000);
                    query.Data.AddRange(response.Data);

                    //await Task.Delay(60);

                    ExtractListOfParameters(response.Data);

                    totalQuantityResult = response.metadata.visible_total_count;
                }
                var xd = totalQuantityResult;
            }

            query.Data = query.Data.GroupBy(o => o.Id).Select(g => g.First()).OrderBy(x => x.created_time).ToList();

            query.metadata = new Metadata();
            query.metadata.visible_total_count = totalQuantityResult;
            query.metadata.total_elements = query.Data.Count;

            //int i = 0;
            //int executedQuery = 0;
            //do
            //{
            //    var response = await GetDefaultResponse(i * limit, limit);
            //    query.Data.AddRange(response.Data);

            //    await Task.Delay(500);
            //    totalQuantityResult = response.metadata.visible_total_count;
            //    //executedQuery += response.Data.Count;
            //    i ++;
            //} while (query.Data.Count < totalQuantityResult);


            query.Data = query.Data.GroupBy(o => o.Id).Select(g => g.First()).OrderBy(x => x.created_time).ToList();

            query.metadata = new Metadata();
            query.metadata.visible_total_count = totalQuantityResult;
            query.metadata.total_elements = query.Data.Count;

            //int i = 0;
            //int executedQuery = 0;
            //do
            //{
            //    var response = await GetDefaultResponse(i * limit, limit);
            //    query.Data.AddRange(response.Data);

            //    await Task.Delay(500);
            //    totalQuantityResult = response.metadata.visible_total_count;
            //    //executedQuery += response.Data.Count;
            //    i ++;
            //} while (query.Data.Count < totalQuantityResult);

         
            return query;
        }

        private void ExtractListOfParameters(List<Data> responses)
        {
            foreach (var response in responses)
            {
                var xdd = ExtractParameter(response.Params, "price_per_m");
            }
        }


        private string ExtractParameter(List<Param> parameters, string parameterName)
        {
            foreach (var param in parameters)
            {
                //if (param.Key == parameterName)
                //{
                //    return param.Value.Key;                    
                //}

                //foreach(var item in param)

                switch (param.Key)
                {
                    case "price_per_m":
                        var PricePerM = param.Value.Key;
                        break;
                    case "floor_select":
                        var FloorSelect = param.Value.Key;
                        break;
                    case "furniture":
                        var Furniture = param.Value.Key;
                        break;
                    case "market":
                        var Market = param.Value.Key;
                        break;
                    case "price":
                        var Price = param.Value.Key; //TODO
                        break;
                    case "builttype":
                        var BuiltType = param.Value.Key;
                        break;
                    case "m":
                        var Area = param.Value.Key;
                        break;
                    case "rooms":
                        var Rooms = param.Value.Key;
                        break;
                }
            }

            return "";
        }
    }
}
