using Newtonsoft.Json;
using OLX_web_api.DTO;
using OLX_web_api.Helper;

namespace OLX_web_api.Services
{
    public class RealEstateServices
    {
        private readonly HTTPClientServices _httpClient;
        //public RealEstateServices()
        //{
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
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 25; i++)
                {
                    var response = await GetDefaultResponse(i * limit, limit, (200000 * j) + 50000, (200000 * j) + 250000);
                    query.Data.AddRange(response.Data);

                    await Task.Delay(60);
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


            return query;
        }
    }
}
