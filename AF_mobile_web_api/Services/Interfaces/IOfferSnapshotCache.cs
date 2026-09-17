namespace AF_mobile_web_api.Services.Interfaces
{
    // The deduplicated offers set the paged list is served from: one entry per Url,
    // its newest snapshot, with the history aggregates of the older ones.
    public interface IOfferSnapshotCache
    {
        /// The current snapshot, loading it if this is the first ask or it has expired.
        /// Concurrent callers share one load instead of each running the scan.
        Task<IReadOnlyList<OfferSnapshot>> GetAsync();

        /// Drops the snapshot; the next ask rebuilds it. Called when a scrape adds rows.
        void Invalidate();
    }
}
