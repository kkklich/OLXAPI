using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplicationDatabase.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyDataIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw SQL prefix indexes instead of HasIndex: these string columns are
            // longtext (Pomelo default), which MySQL cannot index without a prefix
            // length, and we deliberately avoid a risky column-type ALTER on a
            // populated table. The EF model is intentionally left unchanged.
            migrationBuilder.Sql("CREATE INDEX IX_PropertyData_City_AddedRecordTime ON PropertyData (City(100), AddedRecordTime);");
            migrationBuilder.Sql("CREATE INDEX IX_PropertyData_Url ON PropertyData (Url(255));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_PropertyData_City_AddedRecordTime ON PropertyData;");
            migrationBuilder.Sql("DROP INDEX IX_PropertyData_Url ON PropertyData;");
        }
    }
}
