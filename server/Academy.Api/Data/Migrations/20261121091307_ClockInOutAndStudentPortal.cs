using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClockInOutAndStudentPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "check_in_secret",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "geofence_radius_meters",
                table: "tenants",
                type: "integer",
                nullable: false,
                // Existing academies start with the same radius a new one gets.
                defaultValue: 150);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "tenants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "tenants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "student_id",
                table: "app_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "faculty_clocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    faculty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    in_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    in_verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_latitude = table.Column<double>(type: "double precision", nullable: true),
                    in_longitude = table.Column<double>(type: "double precision", nullable: true),
                    in_accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    in_distance_meters = table.Column<double>(type: "double precision", nullable: true),
                    out_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    out_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    out_verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    out_latitude = table.Column<double>(type: "double precision", nullable: true),
                    out_longitude = table.Column<double>(type: "double precision", nullable: true),
                    out_accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    out_distance_meters = table.Column<double>(type: "double precision", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faculty_clocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_faculty_clocks_faculty_faculty_id",
                        column: x => x.faculty_id,
                        principalTable: "faculty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faculty_clocks_faculty_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "faculty_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faculty_clocks_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faculty_clocks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_student_id",
                table: "app_users",
                column: "student_id",
                unique: true,
                filter: "student_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_clocks_faculty_id",
                table: "faculty_clocks",
                column: "faculty_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_clocks_one_running",
                table: "faculty_clocks",
                columns: new[] { "tenant_id", "faculty_id" },
                unique: true,
                filter: "out_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_clocks_session_id",
                table: "faculty_clocks",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_clocks_subject_id",
                table: "faculty_clocks",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_clocks_tenant_id_out_at_utc",
                table: "faculty_clocks",
                columns: new[] { "tenant_id", "out_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_app_users_students_student_id",
                table: "app_users",
                column: "student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_app_users_students_student_id",
                table: "app_users");

            migrationBuilder.DropTable(
                name: "faculty_clocks");

            migrationBuilder.DropIndex(
                name: "ix_app_users_student_id",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "check_in_secret",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "geofence_radius_meters",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "student_id",
                table: "app_users");
        }
    }
}
