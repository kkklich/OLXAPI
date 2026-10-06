using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AF_mobile_web_api.Services
{
    // Builds and caches the getMapPoints response of each city as compressed bytes.
    //
    // The map points are over 99% of what the dashboard serves - 4.1 MB of JSON for
    // Krakow's 2026-09-21 batch, about twice that once Morizon rows carry coordinates - and
    // they only change when a scrape lands. Serialized and Brotli-compressed on every request,
    // they cost ~110 ms of CPU per map opened; compressed here once, a request is a copy.
    public sealed class MapPointsPayloadProvider : IMapPointsPayloadProvider
    {
        // Brotli quality for bytes compressed once and served many times. Measured on Krakow's
        // 4.1 MB of points: q4 467 KB in 52 ms, q6 419 KB in 80 ms, q9 401 KB in 196 ms and
        // q11 340 KB in 8.8 s. The response middleware's per-request setting sent 541 KB. q6
        // is the knee of that curve; q11 would keep the first visitor after a scrape waiting
        // for seconds, on a small instance for tens of seconds.
        private const int BrotliQuality = 6;

        // log2 of the window: 4 MB, the encoder's own default.
        private const int BrotliWindow = 22;

        private readonly IStatisticServices _statistics;
        private readonly IMemoryCache _cache;
        private readonly JsonSerializerOptions _json;
        private readonly ILogger<MapPointsPayloadProvider> _logger;

        public MapPointsPayloadProvider(
            IStatisticServices statistics,
            IMemoryCache cache,
            IOptions<JsonOptions> json,
            ILogger<MapPointsPayloadProvider> logger)
        {
            _statistics = statistics;
            _cache = cache;
            // The options MVC writes its JSON with, so the bytes are exactly what Ok(points)
            // would have sent. That includes its encoder: with none configured, MVC's JSON
            // output formatter writes with the relaxed one, which keeps Polish letters as UTF-8.
            // The serializer's own default escapes each as ó - 6 bytes instead of 2, and
            // ~20 bytes more per point of Krakow's titles and districts.
            var options = json.Value.JsonSerializerOptions;
            _json = options.Encoder is null
                ? new JsonSerializerOptions(options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
                : options;
            _logger = logger;
        }

        // Evicted with the city's other dashboard entries when a scrape of it lands
        // (RealEstateServices), and expiring with them.
        public static string CacheKey(CityEnum city) => $"MapPointsPayload_{city}";

        public async Task<CompressedJson> GetAsync(string cityName)
        {
            var city = StatisticServices.ParseCity(cityName);
            var cacheKey = CacheKey(city);

            if (_cache.TryGetValue(cacheKey, out CompressedJson? cached) && cached is not null)
            {
                return cached;
            }

            var stopwatch = Stopwatch.StartNew();

            var points = await _statistics.GetMapPoints(city.ToString());
            var json = JsonSerializer.SerializeToUtf8Bytes(points, _json);

            var payload = new CompressedJson
            {
                Brotli = CompressBrotli(json),
                Gzip = CompressGzip(json),
                // Over the uncompressed JSON, so it names the data, not one encoding of it, and
                // an unchanged batch keeps its tag across restarts.
                ETag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(json), 0, 16)}\"", isWeak: true),
                JsonLength = json.Length
            };

            _cache.Set(cacheKey, payload, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = StatisticServices.CacheDuration
            });

            _logger.LogInformation(
                "Built map points of {City}: {Points} points, {Json} B of JSON, {Brotli} B br, {Gzip} B gzip in {Elapsed} ms",
                city, points.Count, json.Length, payload.Brotli.Length, payload.Gzip.Length, stopwatch.ElapsedMilliseconds);

            return payload;
        }

        private static byte[] CompressBrotli(byte[] json)
        {
            var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(json.Length)];
            if (!BrotliEncoder.TryCompress(json, buffer, out var written, BrotliQuality, BrotliWindow))
            {
                // GetMaxCompressedLength is the worst case, so this cannot fail on a real payload.
                throw new InvalidOperationException("Brotli compression of the map points did not fit its buffer");
            }

            return buffer.AsSpan(0, written).ToArray();
        }

        // For the few clients without Brotli; it is also where an identity response is
        // decompressed from, so no third copy of the payload is kept.
        private static byte[] CompressGzip(byte[] json)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(json);
            }

            return output.ToArray();
        }
    }
}
