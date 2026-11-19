using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrandingAdmissionsHourlyPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "admissions_open",
                table: "tenants",
                type: "boolean",
                nullable: false,
                // Existing academies start with the form open; the app always writes this column itself.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "logo_version",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tagline",
                table: "tenants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_rate",
                table: "faculty",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "pay_type",
                table: "faculty",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every existing faculty member was on a fixed agreed payout.
                defaultValue: "Package");

            migrationBuilder.CreateTable(
                name: "admission_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    message = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admission_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_admission_applications_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admission_applications_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admission_applications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faculty_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    faculty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taught_on = table.Column<DateOnly>(type: "date", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    hourly_rate = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    voided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faculty_sessions", x => x.id);
                    table.CheckConstraint("ck_faculty_sessions_minutes", "minutes > 0 and minutes <= 1440");
                    table.ForeignKey(
                        name: "fk_faculty_sessions_faculty_faculty_id",
                        column: x => x.faculty_id,
                        principalTable: "faculty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faculty_sessions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_logos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_logos", x => x.tenant_id);
                    table.CheckConstraint("ck_tenant_logos_size", "octet_length(data) <= 204800");
                    table.ForeignKey(
                        name: "fk_tenant_logos_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_applications_course_id",
                table: "admission_applications",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_admission_applications_student_id",
                table: "admission_applications",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_admission_applications_tenant_id_status_created_at_utc",
                table: "admission_applications",
                columns: new[] { "tenant_id", "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_sessions_faculty_id",
                table: "faculty_sessions",
                column: "faculty_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_sessions_tenant_id_faculty_id",
                table: "faculty_sessions",
                columns: new[] { "tenant_id", "faculty_id" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_sessions_tenant_id_taught_on",
                table: "faculty_sessions",
                columns: new[] { "tenant_id", "taught_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admission_applications");

            migrationBuilder.DropTable(
                name: "faculty_sessions");

            migrationBuilder.DropTable(
                name: "tenant_logos");

            migrationBuilder.DropColumn(
                name: "admissions_open",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "logo_version",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "tagline",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "hourly_rate",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "pay_type",
                table: "faculty");
        }
    }
}
