namespace AF_mobile_web_api.Domain
{
    // The rows of a city that PropertyComparer could possibly match to one offer, as a
    // database filter. Everything outside it the comparer rejects anyway, so fetching it
    // only costs transfer: for a 50 m² Krakow offer that was ~30k full rows, of which the
    // ~3k on the same floor and market were the only ones that could ever match.
    //
    // What it leaves out can still be evidence: a Url that the offer's marketplace listed
    // beside it in one scrape is ruled out whole (PropertyComparer.FindMatches), and a row
    // outside the band cannot show the comparer such a scrape. Only a Url whose area, floor
    // or market changed between snapshots has rows on both sides (7,396 of 112,178 Urls in
    // 2026-09); reading its other rows would take a second query per history.
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
        // The comparer requires an identical area, so this is a sub-square-centimeter window
        // around it, wide enough only to survive the float round-trip through the database.
        public double AreaMin { get; init; }
        public double AreaMax { get; init; }
        public int Floor { get; init; }
        public string Market { get; init; } = string.Empty;
    }
}
