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
        public long OffertId { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime CreatedTime { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; }
        public string BuildingType { get; set; }
        public double Area { get; set; }
        public string Description { get; set; }
        public bool Private { get; set; }
        public string City { get; set; }
        public DateTime AddedRecordTime { get; set; }
        public int WebName { get; set; }
    }
}
