using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ForgottenClocksAndAcademyEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_sender_name",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "max_class_minutes",
                table: "tenants",
                type: "integer",
                nullable: false,
                // Three hours, the same as a new academy gets.
                defaultValue: 180);

            migrationBuilder.AddColumn<string>(
                name: "reply_to_email",
                table: "tenants",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "corrected_end_utc",
                table: "faculty_clocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "faculty_clocks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_sent_at_utc",
                table: "faculty_clocks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_sender_name",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "max_class_minutes",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "reply_to_email",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "corrected_end_utc",
                table: "faculty_clocks");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "faculty_clocks");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at_utc",
                table: "faculty_clocks");
        }
    }
}
