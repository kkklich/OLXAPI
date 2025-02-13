namespace OLX_web_api.DTO
{

    public class QueryData
    {
        public List<Data> Data { get; set; }
        public Metadata metadata { get; set; }
        //public Links Links { get; set; }


        public QueryData()
        {
            Data = new List<Data>();
        }
    }

    public class Data
    {
        public long Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        //public DateTime LastRefreshTime { get; set; }
        public DateTime created_time { get; set; }
        //public DateTime ValidToTime { get; set; }
        //public object PushupTime { get; set; }
        //public string Description { get; set; }
        //public Promotion Promotion { get; set; }
        //public List<Param> Params { get; set; }
        //public List<string> KeyParams { get; set; }
        //public bool Business { get; set; }
        //public User User { get; set; }
        //public string Status { get; set; }
        //public Contact Contact { get; set; }
        //public Location Location { get; set; }
        //public List<Photo> Photos { get; set; }
        //public Partner Partner { get; set; }
        //public string ExternalUrl { get; set; }
        //public Category Category { get; set; }
        //public Delivery Delivery { get; set; }
        //public Safedeal Safedeal { get; set; }
        //public Shop Shop { get; set; }
        //public string OfferType { get; set; }
    }

    public class Promotion
    {
        public bool Highlighted { get; set; }
        public bool Urgent { get; set; }
        public bool TopAd { get; set; }
        public List<object> Options { get; set; }
        public bool B2cAdPage { get; set; }
        public bool PremiumAdPage { get; set; }
    }

    public class Param
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public Value Value { get; set; }
    }

    public class Value
    {
        public string Key { get; set; }
        public string Label { get; set; }
    }

    //public class User
    //{
    //    public long Id { get; set; }
    //    public DateTime Created { get; set; }
    //    public bool OtherAdsEnabled { get; set; }
    //    public string Name { get; set; }
    //    public object Logo { get; set; }
    //    public object LogoAdPage { get; set; }
    //    public object SocialNetworkAccountType { get; set; }
    //    public object Photo { get; set; }
    //    public string BannerMobile { get; set; }
    //    public string BannerDesktop { get; set; }
    //    public string CompanyName { get; set; }
    //    public string About { get; set; }
    //    public bool B2cBusinessPage { get; set; }
    //    public bool IsOnline { get; set; }
    //    public DateTime LastSeen { get; set; }
    //    public object SellerType { get; set; }
    //    public string Uuid { get; set; }
    //}

    public class Contact
    {
        public string Name { get; set; }
        public bool Phone { get; set; }
        public bool Chat { get; set; }
        public bool Negotiation { get; set; }
        public bool Courier { get; set; }
    }

    public class Location
    {
        public City City { get; set; }
        public District District { get; set; }
        public Region Region { get; set; }
    }

    public class City
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string NormalizedName { get; set; }
    }

    public class District
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Region
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string NormalizedName { get; set; }
    }

    public class Photo
    {
        public long Id { get; set; }
        public string Filename { get; set; }
        public int Rotation { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Link { get; set; }
    }

    public class Partner
    {
        public string Code { get; set; }
    }

    public class Category
    {
        public int Id { get; set; }
        public string Type { get; set; }
    }

    public class Delivery
    {
        public Rock Rock { get; set; }
    }

    public class Rock
    {
        public object OfferId { get; set; }
        public bool Active { get; set; }
        public string Mode { get; set; }
    }

    public class Safedeal
    {
        public int Weight { get; set; }
        public int WeightGrams { get; set; }
        public string Status { get; set; }
        public bool SafedealBlocked { get; set; }
        public List<object> AllowedQuantity { get; set; }
    }

    public class Shop
    {
        public object Subdomain { get; set; }
    }

    public class Metadata
    {
        public int total_elements { get; set; }
        public int visible_total_count { get; set; }
        //public List<object> Promoted { get; set; }
        //public string SearchId { get; set; }
        //public Adverts Adverts { get; set; }
        //public Source Source { get; set; }
    }

    public class Adverts
    {
        public object Places { get; set; }
        public Config Config { get; set; }
    }

    public class Config
    {
        public Targeting Targeting { get; set; }
    }

    public class Targeting
    {
        public string Env { get; set; }
        public string Lang { get; set; }
        public string Account { get; set; }
        public List<object> Segment { get; set; }
        public string UserStatus { get; set; }
        public string CatL0Id { get; set; }
        public string CatL1Id { get; set; }
        public string CatL2Id { get; set; }
        public string CatL0 { get; set; }
        public string CatL0Path { get; set; }
        public string CatL1 { get; set; }
        public string CatL1Path { get; set; }
        public string CatL2 { get; set; }
        public string CatL2Path { get; set; }
        public string CatL0Name { get; set; }
        public string CatL1Name { get; set; }
        public string CatL2Name { get; set; }
        public string CatId { get; set; }
        public string PrivateBusiness { get; set; }
        public string OfferSeek { get; set; }
        public string View { get; set; }
        public string SearchEngineInput { get; set; }
        public string Page { get; set; }
        public string AppVersion { get; set; }
    }

    public class Source
    {
        public List<int> Organic { get; set; }
    }

    public class Links
    {
        public Self Self { get; set; }
        public Next Next { get; set; }
        public First First { get; set; }
    }

    public class Self
    {
        public string Href { get; set; }
    }

    public class Next
    {
        public string Href { get; set; }
    }

    public class First
    {
        public string Href { get; set; }
    }
}
