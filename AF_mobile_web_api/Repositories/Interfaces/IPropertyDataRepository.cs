using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Repositories.Interfaces
{
    public interface IPropertyDataRepository: IGenericRepository<PropertyData>
    {
        Task<List<PropertyData>> GetPropertiesBySearchResultIdAsync(string city);
        Task SaveMarketplaceDataAsync(List<PropertyData> properties);
    }
}
