using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AcademicYears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_students_tenant_id_created_at_utc",
                table: "students");

            migrationBuilder.AddColumn<Guid>(
                name: "academic_year_id",
                table: "students",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "academic_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_academic_years", x => x.id);
                    table.CheckConstraint("ck_academic_years_dates", "end_date > start_date");
                    table.ForeignKey(
                        name: "fk_academic_years_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(CreateFirstYearPerAcademy);

            migrationBuilder.CreateIndex(
                name: "ix_students_academic_year_id",
                table: "students",
                column: "academic_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_id_academic_year_id_created_at_utc",
                table: "students",
                columns: new[] { "tenant_id", "academic_year_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_academic_years_tenant_id_name",
                table: "academic_years",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_academic_years_tenant_id_start_date",
                table: "academic_years",
                columns: new[] { "tenant_id", "start_date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_students_academic_years_academic_year_id",
                table: "students",
                column: "academic_year_id",
                principalTable: "academic_years",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_students_academic_years_academic_year_id",
                table: "students");

            migrationBuilder.DropTable(
                name: "academic_years");

            migrationBuilder.DropIndex(
                name: "ix_students_academic_year_id",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_students_tenant_id_academic_year_id_created_at_utc",
                table: "students");

            migrationBuilder.DropColumn(
                name: "academic_year_id",
                table: "students");

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_id_created_at_utc",
                table: "students",
                columns: new[] { "tenant_id", "created_at_utc" });
        }
    }
}
