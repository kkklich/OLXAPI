using AF_mobile_web_api.Domain;

namespace AF_mobile_web_api.Services.Interfaces
{
    // The map points of a city's latest scrape as the getMapPoints response, compressed once.
    public interface IMapPointsPayloadProvider
    {
        /// The payload, built the first time it is asked for after a start, a scrape of that
        /// city or the cache's expiry. Throws ArgumentException for an unknown city.
        Task<CompressedJson> GetAsync(string cityName);
    }
}
