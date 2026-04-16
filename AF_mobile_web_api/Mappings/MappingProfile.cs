using AF_mobile_web_api.DTO;
using ApplicationDatabase.Models;
using AutoMapper;

namespace AF_mobile_web_api.Mappings
{
    public class MappingProfile: Profile
    {
        public MappingProfile()
        {
            CreateMap<SearchDataDTO, SearchData>()
                .ForMember(dest => dest.Description, opt => opt.Ignore())
                .ForMember(dest => dest.Location, opt => opt.Ignore())
                .ForMember(dest => dest.Photos, opt => opt.MapFrom(src => new List<Photos>()));

            CreateMap<SearchData, SearchDataDTO>();

            CreateMap<SearchData, PropertyData>()
                .ForMember(x => x.OffertId, opt => opt.MapFrom(src => src.Id))
                .ForMember(x => x.City, opt => opt.MapFrom(src => src.Location.City))
                .ForMember(x => x.AddedRecordTime, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(x => x.WebName, opt => opt.MapFrom(src => (int)src.WebName));
        }
    }
}
