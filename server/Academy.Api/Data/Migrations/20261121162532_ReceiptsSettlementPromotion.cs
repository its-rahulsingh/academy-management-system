using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptsSettlementPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_approve_package_classes",
                table: "tenants",
                type: "boolean",
                nullable: false,
                // On for every academy, as for a new one.
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_student_id",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "html_body",
                table: "email_outbox",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_previous_student_id",
                table: "students",
                column: "previous_student_id",
                unique: true,
                filter: "previous_student_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_students_students_previous_student_id",
                table: "students",
                column: "previous_student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_students_students_previous_student_id",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_students_previous_student_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "auto_approve_package_classes",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "previous_student_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "html_body",
                table: "email_outbox");
        }
    }
}
