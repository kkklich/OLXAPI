using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Services.Interfaces
{
    /// <summary>
    /// Decides whether two scraped rows describe the same real-estate offer.
    /// Rows come from weekly scrape batches (one AddedRecordTime per batch), so the
    /// same offer appears once per batch — possibly with a changed price, and possibly
    /// listed on more than one marketplace (WebName) or re-posted under a new Url.
    /// </summary>
    public interface IPropertyComparer
    {
        /// <summary>True when both rows describe the same offer (across batches or marketplaces).</summary>
        bool AreSameProperty(PropertyData a, PropertyData b);

        /// <summary>
        /// From <paramref name="candidates"/>, returns every row that describes the same
        /// offer as <paramref name="target"/> (the target itself included when present).
        /// </summary>
        List<PropertyData> FindMatches(PropertyData target, IEnumerable<PropertyData> candidates);

        /// <summary>
        /// Partitions rows into groups, one group per distinct real-estate offer,
        /// each group ordered by AddedRecordTime ascending.
        /// </summary>
        List<List<PropertyData>> GroupMatches(IEnumerable<PropertyData> rows);
    }
}
