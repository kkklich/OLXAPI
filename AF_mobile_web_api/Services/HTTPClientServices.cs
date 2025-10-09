using Newtonsoft.Json;
using System.Text;

namespace AF_mobile_web_api.Services
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

        public async Task<TOutput> PostAsync2<TInput, TOutput>(string url, TInput inputData, bool withToken = true, double? timeoutInSeconds = null)
           where TInput : class
           where TOutput : class
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();

                if (timeoutInSeconds.HasValue)
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);
                }

                // Serialize input data to JSON
                var jsonData = JsonConvert.SerializeObject(inputData);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Post,
                    Content = content
                };

                // Add common headers
                request.Headers.Add("Accept", "application/json");

                // Add User-Agent header similar to browsers
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await client.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}.";
                    var errorContent = await resp.Content?.ReadAsStringAsync();
                    if (errorContent != null) throw new HttpRequestException(errorMessage, new Exception(errorContent));
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


        public async Task<TOutput> PostAsync<TInput, TOutput>(string url, TInput inputData, bool withToken = true, double? timeoutInSeconds = null)
           where TInput : class
           where TOutput : class
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();

                if (timeoutInSeconds.HasValue)
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);
                }

                // Serialize input data to JSON
                var jsonData = JsonConvert.SerializeObject(inputData);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Post,
                    Content = content
                };

                // Add common headers
                request.Headers.Add("Accept", "application/json");

                // Add User-Agent header similar to browsers
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await client.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}.";
                    var errorContent = await resp.Content?.ReadAsStringAsync();
                    if (errorContent != null) throw new HttpRequestException(errorMessage, new Exception(errorContent));
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

        public async Task<TOutput> PostAsync<TOutput>(string url, string jsonData, bool withToken = true, double? timeoutInSeconds = null)
            where TOutput : class
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();

                if (timeoutInSeconds.HasValue)
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutInSeconds.Value);
                }

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Post,
                    Content = content
                };

                // Add common headers
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await client.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error while calling : {url}, response StatusCode = {resp.StatusCode}.";
                    var errorContent = await resp.Content?.ReadAsStringAsync();
                    if (errorContent != null) throw new HttpRequestException(errorMessage, new Exception(errorContent));
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


    }
}
