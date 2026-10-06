using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using AF_mobile_web_api.Filters;
using AF_mobile_web_api.Services.Interfaces;
using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RealEstateController : ControllerBase
    {
        private readonly IRealEstateServices _realEstate;
        private readonly IStatisticServices _statisticServices;
        private readonly IPropertyListService _list;
        private readonly IScrapeJobRunner _scrapeRunner;
        private readonly IMapPointsPayloadProvider _mapPoints;
        private readonly ILogger<RealEstateController> _logger;
        public RealEstateController(IRealEstateServices realEstate, IStatisticServices statisticServices, IPropertyListService list, IScrapeJobRunner scrapeRunner, IMapPointsPayloadProvider mapPoints, ILogger<RealEstateController> logger)
        {
            _realEstate = realEstate;
            _statisticServices = statisticServices;
            _list = list;
            _scrapeRunner = scrapeRunner;
            _mapPoints = mapPoints;
            _logger = logger;
        }

        // Single-portal debug scrapes. Run inside the request they took minutes - past every
        // proxy timeout - and could overlap a real scrape, so they go through the background
        // runner like the full scrapes below and share its one-at-a-time flag: 202 when
        // started, 409 while any scrape runs. They save nothing; the offer count goes to the log.
        [HttpGet("nieruchomosciOnline")]
        [RequireScrapeApiKey]
        public IActionResult getNieruchomosciOnlineAPI()
        {
            return StartDebugScrape("NieruchomosciOnline", async services =>
            {
                var result = await services.GetRequiredService<INieruchomosciOnlineService>().GetAllPagesAsync();
                return result?.Data?.Count ?? 0;
            });
        }

        [HttpGet("morizon")]
        [RequireScrapeApiKey]
        public IActionResult getMorizonAPI()
        {
            return StartDebugScrape("Morizon", async services =>
            {
                var result = await services.GetRequiredService<IMorizonApiService>().GetPropertyListingDataAsync();
                return result?.Data?.Count ?? 0;
            });
        }

        // The scrape resolves its portal service from the job's own scope: this controller and
        // its request-scoped services are gone long before the scrape ends.
        private IActionResult StartDebugScrape(string portal, Func<IServiceProvider, Task<int>> scrape)
        {
            var jobName = $"{portal} (debug, not saved)";
            var logger = _logger; // captured on its own so the job does not keep the controller alive

            return _scrapeRunner.TryStart(jobName, async (IServiceProvider services) =>
                {
                    var offers = await scrape(services);
                    logger.LogInformation("Scrape job {Job} returned {Count} offers", jobName, offers);
                })
                ? Accepted(new { message = $"{portal} scrape started" })
                : Conflict(new { message = "A scrape is already running" });
        }

        // A full scrape outlives any reverse-proxy request timeout, so these two endpoints
        // hand the work to the background runner and reply immediately: 202 when started,
        // 409 when a scrape is already in progress (running two at once would write
        // overlapping "latest" batches).
        [HttpGet("loadDataMarkeplaces")]
        [RequireScrapeApiKey]
        public IActionResult LoadDataMarkeplaces()
        {
            return _scrapeRunner.TryStart("LoadDataMarkeplaces", s => s.LoadDataMarkeplacesAsync())
                ? Accepted(new { message = "Scrape started" })
                : Conflict(new { message = "A scrape is already running" });
        }

        [HttpGet("getdataForManyCities")]
        [RequireScrapeApiKey]
        public IActionResult GetdataForManyCities()
        {
            return _scrapeRunner.TryStart("GetdataForManyCities", s => s.GetdataForManyCitiesAsync())
                ? Accepted(new { message = "Scrape started for all cities" })
                : Conflict(new { message = "A scrape is already running" });
        }

        [HttpGet("scrapeStatus")]
        public IActionResult GetScrapeStatus()
        {
            return Ok(new { isRunning = _scrapeRunner.IsRunning });
        }
        
        // Legacy public endpoints, kept for existing callers (DEPLOYMENT.md smoke-tests
        // getUniqueOffers). All four read the dashboard's cached latest batch instead of
        // querying the database per call; an unknown city is a 400.
        [HttpGet("getRealEstate/{city}")]
        public async Task<IActionResult> GetDefaultRealEstate(string city  = "Krakow")
        {
            var result = await _realEstate.GetDataAsync(city);
            return Ok(result);            
        }
                
        [HttpGet("getUniqueOffers")]
        public async Task<IActionResult> getUniqueOffers()
        {
            var result = await _realEstate.GetUniqueOffertsAsync();
            return Ok(result);         
        }
                
        [HttpGet("RealEstateStats")]
        public async Task<IActionResult> GetRealEstateStats()
        {
            var result = await _statisticServices.GetDataWithStatistics();
            return Ok(result);         
        }
        
        [HttpGet("RealEstateGropuBy")]
        public async Task<IActionResult> RealEstateGropuBy([FromQuery] string groupBy)
        {
            var result = await _statisticServices.GetDataWithGroupStatistics(groupBy);
            return Ok(result);            
        }
        
        
        [HttpGet("getTimelinePrice/{city}")]
        public async Task<IActionResult> GetTimelinePrice(string city)
        {
            var result = await _statisticServices.GetTimelinePrice(city);
            return Ok(result);            
        }

        [HttpGet("getGroupedStatistics/{groupBy}/{city}")]
        public async Task<IActionResult> getGroupedStatistics(string groupBy, string city)
        {
            var result = await _statisticServices.GetBarChartData(city, groupBy);
            return Ok(result);         
        }

        [HttpGet("getDashboardCharts/{city}")]
        public async Task<IActionResult> GetDashboardCharts(string city)
        {
            var result = await _statisticServices.GetDashboardCharts(city);
            return Ok(result);
        }

        [HttpGet("getMarketInsights/{city}")]
        public async Task<IActionResult> GetMarketInsights(string city)
        {
            var result = await _statisticServices.GetMarketInsights(city);
            return Ok(result);
        }

        // The biggest response the API serves, sent as the bytes MapPointsPayloadProvider
        // compressed once rather than serialized and compressed per request. no-cache plus the
        // entity tag lets the browser keep a copy and revalidate it: a map reopened after a
        // reload costs a 304 until a scrape changes the points.
        [HttpGet("getMapPoints/{city}")]
        public async Task<IActionResult> GetMapPoints(string city)
        {
            var payload = await _mapPoints.GetAsync(city);

            var headers = Response.GetTypedHeaders();
            headers.ETag = payload.ETag;
            headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            Response.Headers.Vary = HeaderNames.AcceptEncoding;

            var request = Request.GetTypedHeaders();
            if (request.IfNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(payload.ETag, useStrongComparison: false)))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            // Content-Encoding is set here, so the response compression middleware leaves the
            // body alone instead of compressing it a second time.
            if (Accepts(request.AcceptEncoding, "br"))
            {
                Response.Headers.ContentEncoding = "br";
                return File(payload.Brotli, JsonContentType);
            }

            if (Accepts(request.AcceptEncoding, "gzip"))
            {
                Response.Headers.ContentEncoding = "gzip";
                return File(payload.Gzip, JsonContentType);
            }

            // A client that takes neither (curl without --compressed): the plain JSON,
            // decompressed on the way out rather than kept as a third copy.
            return File(new GZipStream(new MemoryStream(payload.Gzip), CompressionMode.Decompress), JsonContentType);
        }

        private const string JsonContentType = "application/json; charset=utf-8";

        // Whether Accept-Encoding allows the coding: its own entry decides, q=0 being a refusal;
        // without one, a "*" entry does.
        private static bool Accepts(IList<StringWithQualityHeaderValue> accepted, string coding)
        {
            var own = accepted.FirstOrDefault(value => StringSegment.Equals(value.Value, coding, StringComparison.OrdinalIgnoreCase));
            if (own != null)
            {
                return (own.Quality ?? 1) > 0;
            }

            var any = accepted.FirstOrDefault(value => value.Value == "*");
            return any != null && (any.Quality ?? 1) > 0;
        }

        [HttpGet("getPriceDrops/{city}")]
        public async Task<IActionResult> GetPriceDrops(string city, [FromQuery] int limit = 20)
        {
            var result = await _statisticServices.GetPriceDrops(city, limit);
            return Ok(result);
        }

        // includeMapPoints=false leaves out the map points, which are over 99% of this payload
        // and are only needed once the visitor opens a map; they are then fetched from
        // getMapPoints/{city}, which shares this endpoint's cache entry.
        [HttpGet("getFullDashboard/{city}")]
        public async Task<IActionResult> GetFullDashboard(string city, [FromQuery] bool includeMapPoints = true)
        {
            var result = await _statisticServices.GetFullDashboardDataAsync(city, includeMapPoints);
            return Ok(result);
        }

        [HttpGet("filterByParameter/{groupBy}/{city}/{parameter}")]
        public async Task<IActionResult> FilterByParameter(string groupBy,  string city,  string parameter)
        {
            var chart = await _statisticServices.FilterByParameter(groupBy, city, parameter);
            return Ok(chart);
        }

        [HttpGet("properties")]
        public async Task<IActionResult> GetProperties([FromQuery] PropertyQueryParams query)
        {
            return Ok(await _list.GetPagedAsync(query));
        }

        [HttpGet("propertyHistory/{city}")]
        public async Task<IActionResult> GetPropertyHistory(string city, [FromQuery] string url)
        {
            var history = await _list.GetHistoryAsync(city, url);
            return history is null ? NotFound() : Ok(history);
        }
    }
}
