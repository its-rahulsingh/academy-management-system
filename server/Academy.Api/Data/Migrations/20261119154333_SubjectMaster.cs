using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubjectMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subject_id",
                table: "faculty_sessions",
                type: "uuid",
                nullable: true);

            // Nullable until existing tests have been pointed at their subject (see CarryOverTypedSubjects).
            migrationBuilder.AddColumn<Guid>(
                name: "subject_id",
                table: "academy_tests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.id);
                    table.ForeignKey(
                        name: "fk_subjects_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "course_subjects",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_subjects", x => new { x.tenant_id, x.course_id, x.subject_id });
                    table.ForeignKey(
                        name: "fk_course_subjects_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_course_subjects_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_course_subjects_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faculty_subjects",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    faculty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faculty_subjects", x => new { x.tenant_id, x.faculty_id, x.subject_id });
                    table.ForeignKey(
                        name: "fk_faculty_subjects_faculty_faculty_id",
                        column: x => x.faculty_id,
                        principalTable: "faculty",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_faculty_subjects_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_faculty_subjects_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            CarryOverTypedSubjects(migrationBuilder);

            migrationBuilder.AlterColumn<Guid>(
                name: "subject_id",
                table: "academy_tests",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "subject",
                table: "academy_tests");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_sessions_subject_id",
                table: "faculty_sessions",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_academy_tests_subject_id",
                table: "academy_tests",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_academy_tests_tenant_id_subject_id",
                table: "academy_tests",
                columns: new[] { "tenant_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_course_subjects_course_id",
                table: "course_subjects",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_subjects_subject_id",
                table: "course_subjects",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_subjects_tenant_id_subject_id",
                table: "course_subjects",
                columns: new[] { "tenant_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_subjects_faculty_id",
                table: "faculty_subjects",
                column: "faculty_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_subjects_subject_id",
                table: "faculty_subjects",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_faculty_subjects_tenant_id_subject_id",
                table: "faculty_subjects",
                columns: new[] { "tenant_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subjects_tenant_id_name",
                table: "subjects",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_academy_tests_subjects_subject_id",
                table: "academy_tests",
                column: "subject_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_faculty_sessions_subjects_subject_id",
                table: "faculty_sessions",
                column: "subject_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Put each test's subject name back as text before the subject master goes.
            migrationBuilder.AddColumn<string>(
                name: "subject",
                table: "academy_tests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("update academy_tests t set subject = s.name from subjects s where s.id = t.subject_id;");

            migrationBuilder.DropForeignKey(
                name: "fk_academy_tests_subjects_subject_id",
                table: "academy_tests");

            migrationBuilder.DropForeignKey(
                name: "fk_faculty_sessions_subjects_subject_id",
                table: "faculty_sessions");

            migrationBuilder.DropTable(
                name: "course_subjects");

            migrationBuilder.DropTable(
                name: "faculty_subjects");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_faculty_sessions_subject_id",
                table: "faculty_sessions");

            migrationBuilder.DropIndex(
                name: "ix_academy_tests_subject_id",
                table: "academy_tests");

            migrationBuilder.DropIndex(
                name: "ix_academy_tests_tenant_id_subject_id",
                table: "academy_tests");

            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "faculty_sessions");

            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "academy_tests");
        }
    }
}
