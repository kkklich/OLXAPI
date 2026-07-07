using Newtonsoft.Json;
using System.Text;

namespace AF_mobile_web_api.Services
{
    public class HTTPClientServices : IHTTPClientServices
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public HTTPClientServices(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<TOutput> Get<TOutput>(string url, bool withToken = true, double? timeoutInSeconds = null)
            where TOutput : class
        {
            var client = _httpClientFactory.CreateClient();
            if (timeoutInSeconds.HasValue) client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await client.SendAsync(request);

            return await HandleResponse<TOutput>(resp, url);
        }

        public async Task<HttpResponseMessage> GetRaw(string url, double? timeoutInSeconds = null, IDictionary<string, string>? headers = null)
        {
            var client = _httpClientFactory.CreateClient();
            if (timeoutInSeconds.HasValue) client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (headers is { Count: > 0 })
            {
                foreach (var kv in headers)
                {
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            var resp = await client.SendAsync(request);
            if (!resp.IsSuccessStatusCode)
            {
                await EnsureSuccess(resp, url);
            }

            return resp;
        }

        public async Task<TOutput> PostAsync<TInput, TOutput>(string url, TInput inputData, bool withToken = true, double? timeoutInSeconds = null)
            where TInput : class
            where TOutput : class
        {
            var jsonData = JsonConvert.SerializeObject(inputData);
            return await PostAsync<TOutput>(url, jsonData, withToken, timeoutInSeconds);
        }

        public async Task<TOutput> PostAsync<TOutput>(string url, string jsonData, bool withToken = true, double? timeoutInSeconds = null)
            where TOutput : class
        {
            var client = _httpClientFactory.CreateClient();
            if (timeoutInSeconds.HasValue) client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);

            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) HttpClientServices/1.0");

            var resp = await client.SendAsync(request);
            return await HandleResponse<TOutput>(resp, url);
        }

        private async Task<TOutput> HandleResponse<TOutput>(HttpResponseMessage resp, string url) where TOutput : class
        {
            await EnsureSuccess(resp, url);
            var result = await resp.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TOutput>(result);
        }

        private async Task EnsureSuccess(HttpResponseMessage resp, string url)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync();
                var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}. Content: {content}";
                throw new HttpRequestException(errorMessage);
            }
        }
    }
}