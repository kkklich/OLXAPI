namespace AF_mobile_web_api.Services
{
    public interface IHTTPClientServices
    {
        Task<TOutput> Get<TOutput>(string url, bool withToken = true, double? timeoutInSeconds = null) where TOutput : class;
        // Returns only a successful response, and hands it over: the caller owns it and must
        // dispose it (`using var`). A failed response is disposed before the exception is thrown.
        Task<HttpResponseMessage> GetRaw(string url, double? timeoutInSeconds = null, IDictionary<string, string>? headers = null);
        Task<TOutput> PostAsync<TInput, TOutput>(string url, TInput inputData, bool withToken = true, double? timeoutInSeconds = null) where TInput : class where TOutput : class;
        Task<TOutput> PostAsync<TOutput>(string url, string jsonData, bool withToken = true, double? timeoutInSeconds = null) where TOutput : class;
    }
}