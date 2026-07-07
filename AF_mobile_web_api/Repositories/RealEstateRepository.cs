using AF_mobile_web_api.Repositories.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;

namespace AF_mobile_web_api.Repositories
{
    public class RealEstateRepository : GenericRepository<WebSearchResults>, IRealEstateRepository
    {
        private readonly AppDbContext _dbContext;

        public RealEstateRepository(AppDbContext dbContext): base(dbContext) 
        {
            _dbContext = dbContext;
        }

        public async Task<WebSearchResults?> GetLatestSearchByCityAsync(string city)
        {
            return await _dbSet
                .Where(w => w.City.ToLower() == city.ToLower())
                .OrderByDescending(w => w.CreationDate)
                .FirstOrDefaultAsync();
        }

        public async Task SaveWebSearchResultAsync(WebSearchResults searchResult)
        {
            await _dbSet.AddAsync(searchResult);
            await _dbContext.SaveChangesAsync();
        }

       
    }
}
