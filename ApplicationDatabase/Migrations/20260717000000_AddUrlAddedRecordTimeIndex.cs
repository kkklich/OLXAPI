using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplicationDatabase.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlAddedRecordTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw SQL prefix index for the same reason as IX_PropertyData_Url: Url is
            // longtext, which MySQL cannot index without a prefix length. The composite
            // replaces the Url-only index - every Url equality lookup can use its leftmost
            // part, and the paged offers list additionally seeks on (Url, AddedRecordTime)
            // for the newest-snapshot join and the FirstSeen/SnapshotCount/FirstPrice
            // subqueries. Created before the drop so Url lookups never lose index cover.
            migrationBuilder.Sql("CREATE INDEX IX_PropertyData_Url_AddedRecordTime ON PropertyData (Url(255), AddedRecordTime);");
            migrationBuilder.Sql("DROP INDEX IX_PropertyData_Url ON PropertyData;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IX_PropertyData_Url ON PropertyData (Url(255));");
            migrationBuilder.Sql("DROP INDEX IX_PropertyData_Url_AddedRecordTime ON PropertyData;");
        }
    }
}
