using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Services.Interfaces
{
    /// <summary>
    /// Decides whether two scraped rows describe the same real-estate offer.
    /// Rows come from weekly scrape batches (one scrape day per batch; historical rows
    /// were stamped per row, so AddedRecordTime is not shared), so the same offer
    /// appears once per batch — possibly with a changed price, and possibly listed on
    /// more than one marketplace (WebName) or re-posted under a new Url.
    /// </summary>
    public interface IPropertyComparer
    {
        /// <summary>
        /// True when both rows describe the same offer (across batches or marketplaces),
        /// judged on the two rows alone.
        /// </summary>
        bool AreSameProperty(PropertyData a, PropertyData b);

        /// <summary>
        /// From <paramref name="candidates"/>, returns every row that describes the same
        /// offer as <paramref name="target"/> (the target itself included when present).
        /// Stricter than AreSameProperty per candidate: a Url that one marketplace listed in
        /// the same scrape as the target's Url is another offer, so none of its rows are
        /// returned. The target's scrapes are read from its Url's rows among the candidates.
        /// </summary>
        List<PropertyData> FindMatches(PropertyData target, IEnumerable<PropertyData> candidates);

        /// <summary>
        /// Partitions rows into groups, one group per distinct real-estate offer,
        /// each group ordered by AddedRecordTime ascending. Rows of two Urls that one
        /// marketplace listed in the same scrape are never linked to each other directly.
        /// </summary>
        List<List<PropertyData>> GroupMatches(IEnumerable<PropertyData> rows);
    }
}
