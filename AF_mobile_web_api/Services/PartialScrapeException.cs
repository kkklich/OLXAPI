using AF_mobile_web_api.Domain;

namespace AF_mobile_web_api.Services
{
    // A failed page must not erase offers already downloaded from healthy pages/ranges.
    public sealed class PartialScrapeException : Exception
    {
        public IReadOnlyList<SearchData> Offers { get; }

        public PartialScrapeException(string message, IReadOnlyList<SearchData> offers, Exception inner)
            : base(message, inner) => Offers = offers;
    }
}
