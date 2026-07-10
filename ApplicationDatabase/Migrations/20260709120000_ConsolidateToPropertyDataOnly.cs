using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplicationDatabase.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateToPropertyDataOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The dashboard now reads everything from PropertyData. The offers are no longer
            // duplicated as a JSON blob, so WebSearchResults is dropped entirely.
            migrationBuilder.DropTable(
                name: "WebSearchResults");

            // Columns nothing reads anymore.
            migrationBuilder.DropColumn(name: "OffertId", table: "PropertyData");
            migrationBuilder.DropColumn(name: "CreatedTime", table: "PropertyData");
            migrationBuilder.DropColumn(name: "Description", table: "PropertyData");

            // Flat location columns the district chart and map now read directly from the row.
            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "PropertyData",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "Lat",
                table: "PropertyData",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Lon",
                table: "PropertyData",
                type: "double",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "District", table: "PropertyData");
            migrationBuilder.DropColumn(name: "Lat", table: "PropertyData");
            migrationBuilder.DropColumn(name: "Lon", table: "PropertyData");

            migrationBuilder.AddColumn<string>(
                name: "OffertId",
                table: "PropertyData",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedTime",
                table: "PropertyData",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PropertyData",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WebSearchResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreationDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    City = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSearchResults", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
