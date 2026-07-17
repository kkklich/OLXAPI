using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Services
{
    // Composes the repository (data access) and the comparer (same-offer identity)
    // into the two read operations the offers UI needs: paged list and price history.
    public class PropertyListService : IPropertyListService
    {
        private readonly IPropertyDataRepository _repo;
        private readonly IPropertyComparer _comparer;

        public PropertyListService(IPropertyDataRepository repo, IPropertyComparer comparer)
        {
            _repo = repo;
            _comparer = comparer;
        }

        public Task<PagedResultDTO<PropertyListItemDTO>> GetPagedAsync(PropertyQueryParams query)
        {
            // The DB stores scraped Polish market names, so the API's English aliases
            // must be translated before filtering; other values pass through so raw
            // stored values ("Pierwotny"/"Wtórny") keep working.
            if (string.Equals(query.Market, "primary", StringComparison.OrdinalIgnoreCase))
            {
                query.Market = "Pierwotny";
            }
            else if (string.Equals(query.Market, "secondary", StringComparison.OrdinalIgnoreCase))
            {
                query.Market = "Wtórny";
            }

            return _repo.GetPagedAsync(query);
        }

        public async Task<PropertyHistoryDTO?> GetHistoryAsync(string city, string url)
        {
            var target = await _repo.GetLatestByUrlAsync(city, url);
            if (target == null)
            {
                return null;
            }

            // Pre-filter candidates by the comparer's ±2% area tolerance so the fuzzy
            // match runs over a small set; a missing area widens the window to everything.
            double areaMin, areaMax;
            if (target.Area <= 0)
            {
                areaMin = 0;
                areaMax = double.MaxValue;
            }
            else
            {
                areaMin = target.Area * 0.98;
                areaMax = target.Area * 1.02;
            }

            var candidates = await _repo.GetHistoryCandidatesAsync(city, url, areaMin, areaMax);

            var matches = _comparer.FindMatches(target, candidates);
            if (matches.Count == 0)
            {
                // The target may not be among the candidates; a history of just the
                // target is still a valid single-point history.
                matches = new List<PropertyData> { target };
            }

            matches = matches
                .OrderBy(m => m.AddedRecordTime)
                .ToList();

            var newest = matches[matches.Count - 1];
            var oldest = matches[0];

            var history = new PropertyHistoryDTO
            {
                Id = newest.Id,
                Url = newest.Url,
                Title = newest.Title,
                City = newest.City,
                District = newest.District,
                Area = newest.Area,

                // Newest-snapshot detail.
                Price = newest.Price,
                PricePerMeter = newest.PricePerMeter,
                Floor = newest.Floor,
                Market = newest.Market,
                BuildingType = newest.BuildingType,
                Private = newest.Private,
                WebName = newest.WebName,
                Lat = newest.Lat,
                Lon = newest.Lon,
                OffertId = newest.OffertId,
                Description = newest.Description,
                CreatedTime = newest.CreatedTime,

                // History aggregates over the matched snapshots (already ordered oldest first).
                FirstSeen = oldest.AddedRecordTime,
                LastSeen = newest.AddedRecordTime,
                SnapshotCount = matches.Count,
                FirstPrice = oldest.Price
            };

            double? previousPrice = null;
            foreach (var row in matches)
            {
                history.Entries.Add(new PropertyHistoryEntryDTO
                {
                    Date = row.AddedRecordTime,
                    Price = row.Price,
                    PricePerMeter = row.PricePerMeter,
                    WebName = row.WebName,
                    Url = row.Url,
                    PriceChange = previousPrice.HasValue ? row.Price - previousPrice.Value : 0
                });

                previousPrice = row.Price;
            }

            return history;
        }
    }
}
