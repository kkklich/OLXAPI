using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Repositories.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;

namespace AF_mobile_web_api.Repositories
{
    public class PropertyDataRepository: GenericRepository<PropertyData>, IPropertyDataRepository
    {
        private readonly AppDbContext _dbContext;
        public PropertyDataRepository(AppDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        // Returns the offers from the most recent scrape (the batch with the newest AddedRecordTime).
        // This is the "current market" snapshot the dashboard charts, insights and map render.
        public async Task<List<PropertyData>> GetLatestByCityAsync(string city)
        {
            var latestBatch = await _dbSet
                .Where(p => p.City == city)
                .MaxAsync(p => (DateTime?)p.AddedRecordTime);

            if (latestBatch == null)
                return new List<PropertyData>();

            return await _dbSet
                .Where(p => p.City == city && p.AddedRecordTime == latestBatch)
                .ToListAsync();
        }

        public async Task<List<TimelineGroup>> GetTimelineByCityAsync(string city)
        {
            return await _dbSet
                .Where(p => p.City == city)
                .GroupBy(p => p.AddedRecordTime.Date)
                .Select(g => new TimelineGroup
                {
                    Date = g.Key,
                    AvgPrice = g.Average(x => x.Price),
                    AvgPricePerMeter = g.Average(x => x.PricePerMeter),
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToListAsync();
        }

        public async Task SaveMarketplaceDataAsync(List<PropertyData> properties)
        {
            await _dbSet.AddRangeAsync(properties);
            await _dbContext.SaveChangesAsync();
        }

        // Paginated listing of distinct offers: the table stores one row per offer per weekly
        // scrape, so the list keeps only the newest snapshot of each Url and aggregates the
        // older ones into history metadata (FirstSeen/FirstPrice/SnapshotCount).
        public async Task<PagedResultDTO<PropertyListItemDTO>> GetPagedAsync(PropertyQueryParams query)
        {
            var q = _dbSet.AsNoTracking()
                .Where(p => p.AddedRecordTime == _dbSet
                    .Where(o => o.Url == p.Url)
                    .Max(o => o.AddedRecordTime));

            if (!string.IsNullOrWhiteSpace(query.City))
                q = q.Where(p => p.City == query.City);
            if (!string.IsNullOrWhiteSpace(query.District))
                q = q.Where(p => p.District == query.District);
            if (!string.IsNullOrWhiteSpace(query.Market))
                q = q.Where(p => p.Market == query.Market);
            if (!string.IsNullOrWhiteSpace(query.BuildingType))
                q = q.Where(p => p.BuildingType == query.BuildingType);
            if (query.WebName.HasValue)
                q = q.Where(p => p.WebName == query.WebName.Value);
            if (query.Private.HasValue)
                q = q.Where(p => p.Private == query.Private.Value);
            if (query.PriceMin.HasValue)
                q = q.Where(p => p.Price >= query.PriceMin.Value);
            if (query.PriceMax.HasValue)
                q = q.Where(p => p.Price <= query.PriceMax.Value);
            if (query.AreaMin.HasValue)
                q = q.Where(p => p.Area >= query.AreaMin.Value);
            if (query.AreaMax.HasValue)
                q = q.Where(p => p.Area <= query.AreaMax.Value);
            if (query.PricePerMeterMin.HasValue)
                q = q.Where(p => p.PricePerMeter >= query.PricePerMeterMin.Value);
            if (query.PricePerMeterMax.HasValue)
                q = q.Where(p => p.PricePerMeter <= query.PricePerMeterMax.Value);
            if (!string.IsNullOrWhiteSpace(query.Search))
                q = q.Where(p => p.Title.Contains(query.Search) || p.District.Contains(query.Search));

            var totalCount = await q.CountAsync();

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var items = await ApplySort(q, query.SortBy, query.SortDir)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PropertyListItemDTO
                {
                    Id = p.Id,
                    Url = p.Url,
                    Title = p.Title,
                    Price = p.Price,
                    PricePerMeter = p.PricePerMeter,
                    Floor = p.Floor,
                    Market = p.Market,
                    BuildingType = p.BuildingType,
                    Area = p.Area,
                    Private = p.Private,
                    WebName = p.WebName,
                    City = p.City,
                    District = p.District,
                    LastSeen = p.AddedRecordTime,
                    FirstSeen = _dbSet.Where(o => o.Url == p.Url).Min(o => o.AddedRecordTime),
                    SnapshotCount = _dbSet.Count(o => o.Url == p.Url),
                    FirstPrice = _dbSet.Where(o => o.Url == p.Url)
                        .OrderBy(o => o.AddedRecordTime)
                        .Select(o => o.Price)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return new PagedResultDTO<PropertyListItemDTO>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // Narrow candidate set for same-offer matching: rows of the same city whose Url
        // matches exactly or whose area is close enough for the fuzzy comparer to decide.
        public async Task<List<PropertyData>> GetHistoryCandidatesAsync(string city, string url, double areaMin, double areaMax)
        {
            return await _dbSet.AsNoTracking()
                .Where(p => p.City == city && (p.Url == url || (p.Area >= areaMin && p.Area <= areaMax)))
                .ToListAsync();
        }

        private static IOrderedQueryable<PropertyData> ApplySort(IQueryable<PropertyData> q, string? sortBy, string? sortDir)
        {
            var desc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

            var ordered = sortBy?.ToLowerInvariant() switch
            {
                "price" => desc ? q.OrderByDescending(p => p.Price) : q.OrderBy(p => p.Price),
                "pricepermeter" => desc ? q.OrderByDescending(p => p.PricePerMeter) : q.OrderBy(p => p.PricePerMeter),
                "area" => desc ? q.OrderByDescending(p => p.Area) : q.OrderBy(p => p.Area),
                "floor" => desc ? q.OrderByDescending(p => p.Floor) : q.OrderBy(p => p.Floor),
                "title" => desc ? q.OrderByDescending(p => p.Title) : q.OrderBy(p => p.Title),
                "city" => desc ? q.OrderByDescending(p => p.City) : q.OrderBy(p => p.City),
                "district" => desc ? q.OrderByDescending(p => p.District) : q.OrderBy(p => p.District),
                "market" => desc ? q.OrderByDescending(p => p.Market) : q.OrderBy(p => p.Market),
                "buildingtype" => desc ? q.OrderByDescending(p => p.BuildingType) : q.OrderBy(p => p.BuildingType),
                _ => desc ? q.OrderByDescending(p => p.AddedRecordTime) : q.OrderBy(p => p.AddedRecordTime),
            };

            return ordered.ThenBy(p => p.Id); // stable order so pages never overlap
        }
    }
}
