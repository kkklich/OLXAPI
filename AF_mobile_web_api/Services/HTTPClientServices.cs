using Newtonsoft.Json;

namespace OLX_web_api.Services
{
    public class HTTPClientServices
    {

        public async Task<TOutput> Get<TOutput>(string url, bool withToken = true, double? timeoutInSeconds = null)
          where TOutput : class
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get
                };

                var resp = await client.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}.";
                    var content = await resp.Content?.ReadAsStringAsync();
                    if (content != null) throw new HttpRequestException(errorMessage, new Exception(content));
                    throw new HttpRequestException(errorMessage);
                }

                var result = await resp.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<TOutput>(result);
            }
            catch (Exception e)
            {
                throw;
            }
        }


        public async Task<HttpResponseMessage> GetRaw(string url, bool withToken = true, double? timeoutInSeconds = null)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get
                };

                var resp = await client.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}.";
                    var content = await resp.Content?.ReadAsStringAsync();
                    if (content != null) throw new HttpRequestException(errorMessage, new Exception(content));
                    throw new HttpRequestException(errorMessage);
                }

                return resp;
            }
            catch (Exception e)
            {
                throw;
            }
        }

    }
}
