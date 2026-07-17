using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationDatabase.Models
{
    public class PropertyData
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; }
        public string BuildingType { get; set; }
        public double Area { get; set; }
        public bool Private { get; set; }
        public int WebName { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }

        // Source-offer metadata retained on the row (originally from the scrape payload).
        public string OffertId { get; set; }
        public string Description { get; set; }
        public DateTime CreatedTime { get; set; }

        // Single timestamp shared by every row of one scrape - acts as the batch marker.
        // Snapshot charts read the latest batch; the timeline groups all batches by day.
        public DateTime AddedRecordTime { get; set; }
    }
}
