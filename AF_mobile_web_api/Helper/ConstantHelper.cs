using Microsoft.AspNetCore.Http;

namespace AF_mobile_web_api.Helper
{
    public class ConstantHelper
    {
        public const string OLXAPI = "https://www.olx.pl/api/v1/offers/";
        public const int RealEstateCategory = 14;
        public const int LesserPolandRegionId = 4;
        public const int KrakowCityId = 8959;

        public const string OLXRealEstate = "?offset=0&limit=40&category_id=14";


        public const string PricePerMeter = "price_per_m";
        public const string FloorSelect = "floor_select";
        public const string Furniture = "furniture";
        public const string Market = "market";
        public const string Price = "price";
        public const string BuildType = "builttype";
        public const string Area = "m";
        public const string Rooms = "rooms";
       
        
        
        
        public const string BuiltTypeBlok = "Blok";
        public const string BuiltTypePozostałe = "Pozostałe";
        public const string BuildingTypeApartament = "Apartament";
        public const string BuildingTypeKamienice = "Kamienice";





        //Morizon
        public const string MorizonAPI = "https://www.morizon.pl/api-morizon";
        public const string BasePhotoUrl = "https://img.morizon.pl/";
        public const string DefaultSearchUrl = "/mieszkania/najtansze/krakow/?ps%5Bprice_from%5D=100000&ps%5Bprice_to%5D=750000";
        public const string GraphqlQuery = @"query getPropertyListingData($url: String!) {
                searchResult: searchProperties(url: $url) {
                # adKeywords
                dataLayer
                hasTopPromoted
                properties {
                nodes {
                addedAt(format: ""dd.MM.y"")
                advertisementText
                area
                contact {
                company {
                address
                faxes
                id
                name
                logo {
                alt
                id
                name
                }
                phones
                type
                }
                person {
                faxes
                name
                phones
                photo {
                alt
                id
                name
                }
                type
                url
                }
                }


                development {
                id
                name
                }
                description (maxLength: 300)
                floorFormatted
                highlightText
                id
                idOnFrontend
                isHighlighted
                isRecommended
                location {
                location
                street,
                number
                }
                numberOfRooms
                #photos {
                #alt
                #id
                #name
                #}
                photosNumber
                promotionPoints
                has3dView
                hasVideo
                plans {
                alt
                id
                name
                }
                price {
                amount
                currency
                }
                priceFormatted
                priceM2 {
                amount
                currency
                }
                priceM2Formatted
                promotionPoints
                title
                url
                }
                totalCount
                }


                }

                }";
    }
}
