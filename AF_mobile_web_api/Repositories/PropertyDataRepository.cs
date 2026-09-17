using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Repositories.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AF_mobile_web_api.Repositories
{
    public class PropertyDataRepository: GenericRepository<PropertyData>, IPropertyDataRepository
    {
        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        public PropertyDataRepository(AppDbContext dbContext, IMemoryCache cache) : base(dbContext)
        {
            _dbContext = dbContext;
            _cache = cache;
        }

        // Returns the offers from the most recent scrape (the latest scrape day).
        // This is the "current market" snapshot the dashboard charts, insights and map render.
        //
        // We select by calendar day, not by an exact AddedRecordTime match: the rows of a
        // single scrape are not guaranteed to share one timestamp. Historical data was stamped
        // with a per-row DateTime.UtcNow, so each offer has a distinct microsecond value (e.g.
        // Katowice's newest scrape holds ~5800 rows spread over ~11:36:28.0092xx). Matching the
        // exact MAX(AddedRecordTime) then returns a single row - the reason the dashboard showed
        // "1 active offer". Grouping by day mirrors GetTimelineByCityAsync and, with weekly
        // scrapes, cleanly isolates the latest run.
        public async Task<List<PropertyData>> GetLatestByCityAsync(string city)
        {
            var latestBatch = await _dbSet
                .Where(p => p.City == city)
                .MaxAsync(p => (DateTime?)p.AddedRecordTime);

            if (latestBatch == null)
                return new List<PropertyData>();

            var dayStart = latestBatch.Value.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet
                .Where(p => p.City == city && p.AddedRecordTime >= dayStart && p.AddedRecordTime < dayEnd)
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
            // The newest-snapshot filter must not be a correlated Max() per row: MySQL
            // re-runs such a subquery for every row of the table (twice - count + page),
            // which took ~55s. One GROUP BY pass over Url joined back on the exact
            // (Url, AddedRecordTime) pair keeps the same rows at a single-scan cost.
            var latestPerUrl = _dbSet
                .GroupBy(o => o.Url)
                .Select(g => new { Url = g.Key, LastSeen = g.Max(o => o.AddedRecordTime) });

            var q = _dbSet.AsNoTracking()
                .Join(latestPerUrl,
                    p => new { p.Url, Time = p.AddedRecordTime },
                    l => new { l.Url, Time = l.LastSeen },
                    (p, l) => p);

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

            // The count costs a full dedup scan but depends only on the filters (not
            // page/sort), and the table changes only when a scrape lands - so a short
            // cache spares that scan while browsing pages of one filter set.
            var countKey = $"PropertyData.PagedCount|{query.City}|{query.District}|{query.Market}|{query.BuildingType}|{query.WebName}|{query.Private}|{query.PriceMin}|{query.PriceMax}|{query.AreaMin}|{query.AreaMax}|{query.PricePerMeterMin}|{query.PricePerMeterMax}|{query.Search}";
            if (!_cache.TryGetValue(countKey, out int totalCount))
            {
                totalCount = await q.CountAsync();
                _cache.Set(countKey, totalCount, TimeSpan.FromMinutes(5));
            }

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            if (totalCount == 0)
            {
                return new PagedResultDTO<PropertyListItemDTO>
                {
                    Items = new List<PropertyListItemDTO>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize
                };
            }

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

        // Target row for a history lookup: the newest snapshot of a given Url in a city.
        public async Task<PropertyData?> GetLatestByUrlAsync(string city, string url)
        {
            return await _dbSet.AsNoTracking()
                .Where(p => p.City == city && p.Url == url)
                .OrderByDescending(p => p.AddedRecordTime)
                .FirstOrDefaultAsync();
        }

        // Narrow candidate set for same-offer matching: rows of the same city whose Url
        // matches exactly or whose area is close enough for the fuzzy comparer to decide.
        public async Task<List<PropertyData>> GetHistoryCandidatesAsync(string city, string url, double areaMin, double areaMax)
        {
            return await _dbSet.AsNoTracking()
                .Where(p => p.City == city && (p.Url == url || (p.Area >= areaMin && p.Area <= areaMax)))
                .ToListAsync();
        }

        // Offers whose price fell from the previous scrape to the newest one.
        //
        // Weekly scrapes make "the previous price" simply the offer's price on the
        // previous scrape day, so instead of grouping over the whole per-Url history
        // (a plan heavy enough to time out on this table), we pull just two bounded
        // single-day slices - the latest scrape day and the one before it - and diff
        // them by Url in memory. Each slice is one indexed range scan; the join is a
        // few thousand rows. An offer that skipped last week's scrape simply has no
        // "previous" and is left out, which is the right behaviour for a weekly card.
        public async Task<List<PriceDropDTO>> GetPriceDropsAsync(string city, int limit)
        {
            var latestBatch = await _dbSet
                .Where(p => p.City == city)
                .MaxAsync(p => (DateTime?)p.AddedRecordTime);

            if (latestBatch == null)
                return new List<PriceDropDTO>();

            var dayStart = latestBatch.Value.Date;
            var dayEnd = dayStart.AddDays(1);

            // The scrape day immediately before the latest one - the baseline to diff against.
            var prevBatch = await _dbSet
                .Where(p => p.City == city && p.AddedRecordTime < dayStart)
                .MaxAsync(p => (DateTime?)p.AddedRecordTime);

            if (prevBatch == null)
                return new List<PriceDropDTO>();

            var prevStart = prevBatch.Value.Date;
            var prevEnd = prevStart.AddDays(1);

            // Project only the columns the diff needs: full rows drag the Description
            // longtext (and other unused columns) of ~6k offers per day over the wire,
            // which made the uncached request ~25-40% slower. (Diffing in SQL is far slower -
            // MySQL joins the longtext Urls of the two slices poorly, ~20s - and fetching
            // display columns in a second by-Id query for the top drops only didn't pay off.)
            var currentDay = await _dbSet
                .Where(p => p.City == city && p.Price > 0
                    && p.AddedRecordTime >= dayStart && p.AddedRecordTime < dayEnd)
                .Select(p => new
                {
                    p.Url,
                    p.Title,
                    p.District,
                    p.Area,
                    p.WebName,
                    p.Price,
                    p.PricePerMeter,
                    p.AddedRecordTime
                })
                .ToListAsync();

            var previousDay = await _dbSet
                .Where(p => p.City == city && p.Price > 0
                    && p.AddedRecordTime >= prevStart && p.AddedRecordTime < prevEnd)
                .Select(p => new { p.Url, p.Price })
                .ToListAsync();

            // Previous price per Url (an offer lists once per portal per scrape; if a Url
            // somehow repeats within the day, the highest earlier price is the baseline).
            var previousPriceByUrl = previousDay
                .GroupBy(p => p.Url)
                .ToDictionary(g => g.Key, g => g.Max(p => p.Price));

            var drops = new List<PriceDropDTO>();
            foreach (var current in currentDay)
            {
                if (!previousPriceByUrl.TryGetValue(current.Url, out var previousPrice))
                    continue;
                if (previousPrice <= current.Price)
                    continue;

                drops.Add(new PriceDropDTO
                {
                    Url = current.Url,
                    Title = current.Title,
                    City = city,
                    District = current.District,
                    Area = current.Area,
                    WebName = current.WebName,
                    CurrentPrice = current.Price,
                    PreviousPrice = previousPrice,
                    CurrentPricePerMeter = current.PricePerMeter,
                    LastSeen = current.AddedRecordTime,
                    PreviousSeen = prevBatch.Value
                });
            }

            // De-duplicate Urls that appear more than once in the latest day (keep the
            // biggest drop), then rank by relative drop.
            return drops
                .GroupBy(d => d.Url)
                .Select(g => g.OrderByDescending(d => d.DropPercent).First())
                .OrderByDescending(d => d.DropPercent)
                .Take(limit)
                .ToList();
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
