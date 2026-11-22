using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformUsageAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "database_size_days",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_database_size_days", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "platform_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "usage_hours",
                columns: table => new
                {
                    hour_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    server_errors = table.Column<int>(type: "integer", nullable: false),
                    client_errors = table.Column<int>(type: "integer", nullable: false),
                    total_ms = table.Column<long>(type: "bigint", nullable: false),
                    max_ms = table.Column<int>(type: "integer", nullable: false),
                    under100_ms = table.Column<int>(type: "integer", nullable: false),
                    under300_ms = table.Column<int>(type: "integer", nullable: false),
                    under1s = table.Column<int>(type: "integer", nullable: false),
                    under3s = table.Column<int>(type: "integer", nullable: false),
                    over3s = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_hours", x => new { x.hour_utc, x.tenant_id, x.feature, x.role, x.device });
                });

            migrationBuilder.CreateTable(
                name: "usage_peak_hours",
                columns: table => new
                {
                    hour_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    peak_per_minute = table.Column<int>(type: "integer", nullable: false),
                    peak_minute_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_peak_hours", x => x.hour_utc);
                });

            migrationBuilder.CreateTable(
                name: "usage_user_hours",
                columns: table => new
                {
                    hour_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_user_hours", x => new { x.hour_utc, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_hours_tenant_id_hour_utc",
                table: "usage_hours",
                columns: new[] { "tenant_id", "hour_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_user_hours_tenant_id_hour_utc",
                table: "usage_user_hours",
                columns: new[] { "tenant_id", "hour_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "database_size_days");

            migrationBuilder.DropTable(
                name: "platform_settings");

            migrationBuilder.DropTable(
                name: "usage_hours");

            migrationBuilder.DropTable(
                name: "usage_peak_hours");

            migrationBuilder.DropTable(
                name: "usage_user_hours");
        }
    }
}
