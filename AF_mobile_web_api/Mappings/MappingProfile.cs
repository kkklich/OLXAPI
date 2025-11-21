using AF_mobile_web_api.DTO;
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
        }
    }
}
