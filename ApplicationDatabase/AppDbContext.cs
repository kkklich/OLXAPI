using ApplicationDatabase.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationDatabase
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<PropertyData> PropertyData { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Query indexes for the paged offers list (GetPagedAsync). Only non-string
            // columns are declared here: the string columns (City, Url) are longtext and
            // carry raw-SQL prefix indexes created by hand-written migrations
            // (20260713 AddPropertyDataIndexes, 20260717 AddUrlAddedRecordTimeIndex),
            // which MySQL cannot express through HasIndex without a column-type change.
            modelBuilder.Entity<PropertyData>(entity =>
            {
                // Default sort of the offers list is AddedRecordTime DESC with no
                // mandatory filter; lets MySQL read rows in sort order instead of
                // filesorting the whole join result.
                entity.HasIndex(p => p.AddedRecordTime);

                // WebName (scrape source) is a typical list filter; paired with the
                // default sort column so the filtered listing stays in index order.
                entity.HasIndex(p => new { p.WebName, p.AddedRecordTime });
            });
        }
    }
}
