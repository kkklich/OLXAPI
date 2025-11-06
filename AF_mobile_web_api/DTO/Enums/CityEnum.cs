namespace AF_mobile_web_api.DTO.Enums
{
    public enum CityEnum
    {
        Krakow,
        Katowice
    }

    public static class CityExtensions
    {
        private static readonly Dictionary<CityEnum, string> cityValues = new()
        {
            { CityEnum.Krakow, "Krak%C3%B3w" },
            { CityEnum.Katowice, "Katowice" }
        };

        public static string ToEncodedString(this CityEnum city)
        {
            return cityValues.TryGetValue(city, out var value) ? value : city.ToString();
        }

        private static readonly Dictionary<CityEnum, int> cityOLXValues = new()
        {
            { CityEnum.Krakow, 8959 },
            { CityEnum.Katowice, 7691 }
        };

        public static int ToEncodedOLXString(this CityEnum city)
        {
            return cityOLXValues.TryGetValue(city, out var value) ? value : 0;
        }
        
        private static readonly Dictionary<CityEnum, int> regionOLXValues = new()
        {
            { CityEnum.Krakow, 4 },
            { CityEnum.Katowice, 6}
        };

        public static int ToEncodedRegionOLXString(this CityEnum city)
        {
            return regionOLXValues.TryGetValue(city, out var value) ? value : 0;
        }
    }
   
}
