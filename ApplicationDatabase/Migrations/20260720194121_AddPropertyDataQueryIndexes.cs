using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplicationDatabase.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyDataQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PropertyData_AddedRecordTime",
                table: "PropertyData",
                column: "AddedRecordTime");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyData_WebName_AddedRecordTime",
                table: "PropertyData",
                columns: new[] { "WebName", "AddedRecordTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PropertyData_AddedRecordTime",
                table: "PropertyData");

            migrationBuilder.DropIndex(
                name: "IX_PropertyData_WebName_AddedRecordTime",
                table: "PropertyData");
        }
    }
}
