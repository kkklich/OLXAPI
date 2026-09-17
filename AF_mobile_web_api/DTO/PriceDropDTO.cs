namespace AF_mobile_web_api.DTO
{
    // One offer whose price fell between its previous snapshot and its newest one
    // (the latest scrape day). The append-only table already records every weekly
    // price per Url; this is the diff of the two most recent snapshots of an offer.
    public class PriceDropDTO
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public double Area { get; set; }
        public int WebName { get; set; }

        // Newest snapshot (latest scrape day) vs the one before it.
        public double CurrentPrice { get; set; }
        public double PreviousPrice { get; set; }
        public double CurrentPricePerMeter { get; set; }

        public DateTime PreviousSeen { get; set; }
        public DateTime LastSeen { get; set; }

        // Always positive for a drop (PreviousPrice > CurrentPrice by construction).
        public double DropAmount => PreviousPrice - CurrentPrice;
        public double DropPercent => PreviousPrice > 0 ? (PreviousPrice - CurrentPrice) / PreviousPrice * 100 : 0;
    }
}
