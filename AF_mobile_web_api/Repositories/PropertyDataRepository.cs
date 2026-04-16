using AF_mobile_web_api.Repositories.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;

namespace AF_mobile_web_api.Repositories
{
    public class PropertyDataRepository: GenericRepository<PropertyData>, IPropertyDataRepository
    {
        private readonly AppDbContext _dbContext;
        public PropertyDataRepository(AppDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<PropertyData>> GetPropertiesBySearchResultIdAsync(string city)
        {
            return await _dbSet
                .Where(p => p.City == city.ToString())
                .ToListAsync();
        }

        public async Task SaveMarketplaceDataAsync(List<PropertyData> properties)
        {
            await _dbSet.AddRangeAsync(properties);
            await _dbContext.SaveChangesAsync();
        }
    }
}
