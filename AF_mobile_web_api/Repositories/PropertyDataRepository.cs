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

        // The distinct offers of the whole table: one row per Url - its newest snapshot -
        // carrying the history aggregates of every snapshot that shares that Url.
        //
        // The newest-snapshot filter must not be a correlated Max() per row: MySQL re-runs
        // such a subquery for every row of the table, which took ~55s. One GROUP BY pass
        // over Url joined back on the exact (Url, AddedRecordTime) pair keeps the same rows
        // at a single-scan cost.
        //
        // This runs once per scrape rather than once per request - OfferSnapshotCache holds
        // the result and the list filters it in memory. Streamed, not returned as a list,
        // so the rows and the snapshot built from them are not both in memory at the peak.
        public IAsyncEnumerable<LatestOfferRow> StreamLatestOffersAsync()
        {
            var latestPerUrl = _dbSet
                .GroupBy(o => o.Url)
                .Select(g => new
                {
                    Url = g.Key,
                    LastSeen = g.Max(o => o.AddedRecordTime),
                    FirstSeen = g.Min(o => o.AddedRecordTime),
                    SnapshotCount = g.Count()
                });

            // Url and Title are left out on purpose: they are the widest columns and are
            // only needed for the rows one page actually renders (GetPageDetailsAsync).
            return _dbSet.AsNoTracking()
                .Join(latestPerUrl,
                    p => new { p.Url, Time = p.AddedRecordTime },
                    l => new { l.Url, Time = l.LastSeen },
                    (p, l) => new LatestOfferRow
                    {
                        Id = p.Id,
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
                        Title = p.Title,
                        LastSeen = p.AddedRecordTime,
                        FirstSeen = l.FirstSeen,
                        SnapshotCount = l.SnapshotCount
                    })
                .AsAsyncEnumerable();
        }

        // The display-only columns of the offers on one page, by primary key: the Url and
        // Title kept out of the in-memory snapshot, plus the price of the oldest snapshot
        // of that Url (the "first price" the list shows the change against).
        public async Task<List<OfferPageDetail>> GetPageDetailsAsync(IReadOnlyList<Guid> ids)
        {
            if (ids.Count == 0)
                return new List<OfferPageDetail>();

            return await _dbSet.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new OfferPageDetail
                {
                    Id = p.Id,
                    Url = p.Url,
                    Title = p.Title,
                    FirstPrice = _dbSet.Where(o => o.Url == p.Url)
                        .OrderBy(o => o.AddedRecordTime)
                        .Select(o => o.Price)
                        .FirstOrDefault()
                })
                .ToListAsync();
        }

        // Target row for a history lookup: the newest snapshot of a given Url in a city.
        public async Task<PropertyData?> GetLatestByUrlAsync(string city, string url)
        {
            return await _dbSet.AsNoTracking()
                .Where(p => p.City == city && p.Url == url)
                .OrderByDescending(p => p.AddedRecordTime)
                .FirstOrDefaultAsync();
        }

        // Narrow candidate set for same-offer matching: the rows of a city the comparer could
        // accept - by Url, or by meeting its fuzzy rules' hard requirements. See
        // HistoryCandidateFilter. The comparer still makes every decision; this only decides
        // what is worth sending over the wire for it to look at.
        public async Task<List<PropertyData>> GetHistoryCandidatesAsync(string city, HistoryCandidateFilter filter)
        {
            var rows = _dbSet.AsNoTracking().Where(p => p.City == city);

            // LIKE rather than StartsWith: EF turns StartsWith into LEFT(Url, n) = @prefix,
            // which copies a slice of the longtext out of every row the scan visits, while
            // LIKE stops at the first character that differs. The prefix is not escaped on
            // purpose - a '_' or '%' in it (both common in Urls) only widens the match, and
            // the comparer rejects whatever does not normalize to the same Url. Backslash is
            // the one character that must be escaped: it is LIKE's escape character, so left
            // alone it would narrow the match instead.
            var pattern = filter.UrlPrefix is null ? null : filter.UrlPrefix.Replace(@"\", @"\\") + "%";

            if (filter.Fuzzy is { } fuzzy)
            {
                var (areaMin, areaMax, floor) = (fuzzy.AreaMin, fuzzy.AreaMax, fuzzy.Floor);
                // Trimmed like the comparer's TextEquals; the collation already makes the SQL
                // side case- and accent-insensitive, a superset of what the comparer accepts.
                var market = fuzzy.Market.Trim();

                rows = pattern is null
                    ? rows.Where(p => p.Area >= areaMin && p.Area <= areaMax && p.Floor == floor && p.Market.Trim() == market)
                    : rows.Where(p => EF.Functions.Like(p.Url, pattern)
                        || (p.Area >= areaMin && p.Area <= areaMax && p.Floor == floor && p.Market.Trim() == market));
            }
            else if (pattern is not null)
            {
                rows = rows.Where(p => EF.Functions.Like(p.Url, pattern));
            }
            else
            {
                return new List<PropertyData>();
            }

            return await rows.ToListAsync();
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

    }
}
