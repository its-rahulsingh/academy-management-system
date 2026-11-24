using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlanWebsiteListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "highlighted",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_on_website",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // The website marked Basic "Most popular" before prices came from the plans.
            migrationBuilder.Sql("UPDATE plans SET highlighted = true WHERE code = 'basic';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "highlighted",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "show_on_website",
                table: "plans");
        }
    }
}
