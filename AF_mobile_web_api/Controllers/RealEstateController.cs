using Microsoft.AspNetCore.Mvc;
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
        private readonly IMorizonApiService _morizonApiService;
        private readonly INieruchomosciOnlineService _nieruchomosciOnlineService;
        private readonly IPropertyListService _list;
        private readonly IScrapeJobRunner _scrapeRunner;
        public RealEstateController(IRealEstateServices realEstate, IStatisticServices statisticServices, IMorizonApiService morizonApiService, INieruchomosciOnlineService nieruchomosciOnlineService, IPropertyListService list, IScrapeJobRunner scrapeRunner)
        {
            _realEstate = realEstate;
            _statisticServices = statisticServices;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _list = list;
            _scrapeRunner = scrapeRunner;
        }

        [HttpGet("nieruchomosciOnline")]
        [RequireScrapeApiKey]
        public async Task<IActionResult> getNieruchomosciOnlineAPI()
        {
            var result = await _nieruchomosciOnlineService.GetAllPagesAsync();
            return Ok(result);           
        }
        
        [HttpGet("morizon")]
        [RequireScrapeApiKey]
        public async Task<IActionResult> getMorizonAPI()
        {
            var result = await _morizonApiService.GetPropertyListingDataAsync();
            return Ok(result);           
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

        [HttpGet("getMapPoints/{city}")]
        public async Task<IActionResult> GetMapPoints(string city)
        {
            var result = await _statisticServices.GetMapPoints(city);
            return Ok(result);
        }

        [HttpGet("getFullDashboard/{city}")]
        public async Task<IActionResult> GetFullDashboard(string city)
        {
            var result = await _statisticServices.GetFullDashboardDataAsync(city);
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
