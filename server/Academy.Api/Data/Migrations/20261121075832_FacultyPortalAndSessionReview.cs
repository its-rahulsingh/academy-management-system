using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FacultyPortalAndSessionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "faculty_sessions",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review",
                table: "faculty_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every session logged before the portal existed was entered by staff, so it is already approved.
                defaultValue: "Approved");

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "faculty_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at_utc",
                table: "faculty_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "faculty_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "faculty_sessions",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "faculty_id",
                table: "app_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_faculty_sessions_tenant_id_review",
                table: "faculty_sessions",
                columns: new[] { "tenant_id", "review" });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_faculty_id",
                table: "app_users",
                column: "faculty_id",
                unique: true,
                filter: "faculty_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_app_users_faculty_faculty_id",
                table: "app_users",
                column: "faculty_id",
                principalTable: "faculty",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_app_users_faculty_faculty_id",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "ix_faculty_sessions_tenant_id_review",
                table: "faculty_sessions");

            migrationBuilder.DropIndex(
                name: "ix_app_users_faculty_id",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "review",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "reviewed_at_utc",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "faculty_id",
                table: "app_users");
        }
    }
}
