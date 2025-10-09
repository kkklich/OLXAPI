using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplicationDatabase.Migrations
{
    /// <inheritdoc />
    public partial class addwebNamePropertyData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WebName",
                table: "PropertyData",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebName",
                table: "PropertyData");
        }
    }
}
