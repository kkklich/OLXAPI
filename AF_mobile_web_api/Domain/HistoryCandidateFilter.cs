namespace AF_mobile_web_api.Domain
{
    // The rows of a city that PropertyComparer could possibly match to one offer, as a
    // database filter. Everything outside it the comparer rejects anyway, so fetching it
    // only costs transfer: for a 50 m² Krakow offer that was ~30k full rows, of which the
    // ~3k on the same floor and market were the only ones that could ever match.
    public class HistoryCandidateFilter
    {
        // Rows whose Url could normalize to the offer's. The comparer ignores the query
        // string, fragment and trailing slash (81k rows carry a query string), so an exact
        // Url match would miss the same address listed with different parameters. Null when
        // the offer's Url normalizes to nothing - the comparer then never matches by Url.
        public string? UrlPrefix { get; init; }

        // The fuzzy rules' hard requirements. Null when the offer has no area: the comparer
        // never fuzzy-matches without one, so only its Url can find it a history.
        public FuzzyCandidateBand? Fuzzy { get; init; }
    }

    public class FuzzyCandidateBand
    {
        public double AreaMin { get; init; }
        public double AreaMax { get; init; }
        public int Floor { get; init; }
        public string Market { get; init; } = string.Empty;
    }
}
