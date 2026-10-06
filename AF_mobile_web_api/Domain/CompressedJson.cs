using Microsoft.Net.Http.Headers;

namespace AF_mobile_web_api.Domain
{
    // A JSON response serialized and compressed once, then served as bytes.
    //
    // For a payload of megabytes that changes once a week, serializing it and compressing it
    // again on every request was most of what the request cost. The bytes live as long as
    // the data they were made from, and the entity tag lets a browser that already holds
    // them get a 304 instead of the payload.
    public sealed class CompressedJson
    {
        public required byte[] Brotli { get; init; }
        public required byte[] Gzip { get; init; }

        /// Weak: the same tag stands for both encodings, which is only correct for a weak
        /// validator. If-None-Match compares weakly anyway, so revalidation still works.
        public required EntityTagHeaderValue ETag { get; init; }

        /// Size of the JSON before compression, for the logs.
        public required int JsonLength { get; init; }
    }
}
