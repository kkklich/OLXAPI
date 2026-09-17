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
        private readonly IOfferSnapshotCache _offers;

        public PropertyListService(IPropertyDataRepository repo, IPropertyComparer comparer, IOfferSnapshotCache offers)
        {
            _repo = repo;
            _comparer = comparer;
            _offers = offers;
        }

        // Filtering, sorting and paging happen over the in-memory offers snapshot; only the
        // columns the page renders (Url, Title, FirstPrice) are read from the database, by
        // primary key. See OfferQuery for why the query this replaced could not stay in SQL.
        public async Task<PagedResultDTO<PropertyListItemDTO>> GetPagedAsync(PropertyQueryParams query)
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

            var offers = await _offers.GetAsync();
            var page = OfferQuery.Apply(offers, query);

            var details = (await _repo.GetPageDetailsAsync(page.Items.Select(o => o.Id).ToList()))
                .ToDictionary(d => d.Id);

            var items = page.Items.Select(offer =>
            {
                details.TryGetValue(offer.Id, out var detail);

                return new PropertyListItemDTO
                {
                    Id = offer.Id,
                    Url = detail?.Url ?? string.Empty,
                    Title = detail?.Title ?? string.Empty,
                    Price = offer.Price,
                    PricePerMeter = offer.PricePerMeter,
                    Floor = offer.Floor,
                    Market = offer.Market,
                    BuildingType = offer.BuildingType,
                    Area = offer.Area,
                    Private = offer.Private,
                    WebName = offer.WebName,
                    City = offer.City,
                    District = offer.District,
                    LastSeen = offer.LastSeen,
                    FirstSeen = offer.FirstSeen,
                    SnapshotCount = offer.SnapshotCount,
                    // No detail row means the offer vanished between the snapshot and now,
                    // which only a reset database can do; its own price is the honest
                    // "unchanged" answer, rather than a change measured against zero.
                    FirstPrice = detail?.FirstPrice ?? offer.Price
                };
            }).ToList();

            return new PagedResultDTO<PropertyListItemDTO>
            {
                Items = items,
                TotalCount = page.TotalCount,
                Page = page.Page,
                PageSize = page.PageSize
            };
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
