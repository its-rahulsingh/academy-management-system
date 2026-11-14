using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UseUtcForDateConversion);

            migrationBuilder.DropForeignKey(
                name: "FK_academy_tests_courses_CourseId",
                table: "academy_tests");

            migrationBuilder.DropForeignKey(
                name: "FK_faculty_payouts_faculty_FacultyId",
                table: "faculty_payouts");

            migrationBuilder.DropForeignKey(
                name: "FK_student_payments_students_StudentId",
                table: "student_payments");

            migrationBuilder.DropForeignKey(
                name: "FK_students_courses_CourseId",
                table: "students");

            migrationBuilder.DropForeignKey(
                name: "FK_test_results_academy_tests_TestId",
                table: "test_results");

            migrationBuilder.DropForeignKey(
                name: "FK_test_results_students_StudentId",
                table: "test_results");

            migrationBuilder.DropPrimaryKey(
                name: "PK_test_results",
                table: "test_results");

            migrationBuilder.DropPrimaryKey(
                name: "PK_students",
                table: "students");

            migrationBuilder.DropIndex(
                name: "IX_students_Email",
                table: "students");

            migrationBuilder.DropPrimaryKey(
                name: "PK_student_payments",
                table: "student_payments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_faculty_payouts",
                table: "faculty_payouts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_faculty",
                table: "faculty");

            migrationBuilder.DropPrimaryKey(
                name: "PK_expenses",
                table: "expenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_courses",
                table: "courses");

            migrationBuilder.DropIndex(
                name: "IX_courses_Name",
                table: "courses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_batches",
                table: "batches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_app_users",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "IX_app_users_Email",
                table: "app_users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_academy_tests",
                table: "academy_tests");

            migrationBuilder.RenameColumn(
                name: "Remarks",
                table: "test_results",
                newName: "remarks");

            migrationBuilder.RenameColumn(
                name: "Marks",
                table: "test_results",
                newName: "marks");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "test_results",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TestId",
                table: "test_results",
                newName: "test_id");

            migrationBuilder.RenameColumn(
                name: "StudentId",
                table: "test_results",
                newName: "student_id");

            migrationBuilder.RenameIndex(
                name: "IX_test_results_TestId_StudentId",
                table: "test_results",
                newName: "ix_test_results_test_id_student_id");

            migrationBuilder.RenameIndex(
                name: "IX_test_results_StudentId",
                table: "test_results",
                newName: "ix_test_results_student_id");

            migrationBuilder.RenameColumn(
                name: "Remarks",
                table: "students",
                newName: "remarks");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "students",
                newName: "phone");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "students",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Active",
                table: "students",
                newName: "active");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "students",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "students",
                newName: "full_name");

            migrationBuilder.RenameColumn(
                name: "FeeAmount",
                table: "students",
                newName: "fee_amount");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "students",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CourseId",
                table: "students",
                newName: "course_id");

            migrationBuilder.RenameColumn(
                name: "AdmissionDate",
                table: "students",
                newName: "admission_date");

            migrationBuilder.RenameIndex(
                name: "IX_students_CourseId",
                table: "students",
                newName: "ix_students_course_id");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "student_payments",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Method",
                table: "student_payments",
                newName: "method");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "student_payments",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "student_payments",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "StudentId",
                table: "student_payments",
                newName: "student_id");

            migrationBuilder.RenameColumn(
                name: "PaidOn",
                table: "student_payments",
                newName: "paid_on");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "student_payments",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_student_payments_StudentId",
                table: "student_payments",
                newName: "ix_student_payments_student_id");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "faculty_payouts",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Method",
                table: "faculty_payouts",
                newName: "method");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "faculty_payouts",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "faculty_payouts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "PaidOn",
                table: "faculty_payouts",
                newName: "paid_on");

            migrationBuilder.RenameColumn(
                name: "FacultyId",
                table: "faculty_payouts",
                newName: "faculty_id");

            migrationBuilder.RenameIndex(
                name: "IX_faculty_payouts_FacultyId",
                table: "faculty_payouts",
                newName: "ix_faculty_payouts_faculty_id");

            migrationBuilder.RenameColumn(
                name: "Specialization",
                table: "faculty",
                newName: "specialization");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "faculty",
                newName: "phone");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "faculty",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Active",
                table: "faculty",
                newName: "active");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "faculty",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "faculty",
                newName: "full_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "faculty",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "AgreedPayout",
                table: "faculty",
                newName: "agreed_payout");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "expenses",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "expenses",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Method",
                table: "expenses",
                newName: "method");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "expenses",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "expenses",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "expenses",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "SpentOn",
                table: "expenses",
                newName: "spent_on");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "expenses",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "courses",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Fee",
                table: "courses",
                newName: "fee");

            migrationBuilder.RenameColumn(
                name: "Active",
                table: "courses",
                newName: "active");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "courses",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "courses",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "app_users",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "app_users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Active",
                table: "app_users",
                newName: "active");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "app_users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "app_users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "app_users",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "app_users",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "academy_tests",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Subject",
                table: "academy_tests",
                newName: "subject");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "academy_tests",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "academy_tests",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TestDate",
                table: "academy_tests",
                newName: "test_date");

            migrationBuilder.RenameColumn(
                name: "MaxMarks",
                table: "academy_tests",
                newName: "max_marks");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "academy_tests",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CourseId",
                table: "academy_tests",
                newName: "course_id");

            migrationBuilder.RenameIndex(
                name: "IX_academy_tests_CourseId",
                table: "academy_tests",
                newName: "ix_academy_tests_course_id");

            migrationBuilder.CreateSequence(
                name: "subscription_invoice_number_seq");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at_utc",
                table: "test_results",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "test_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "test_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "students",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "admission_date",
                table: "students",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "students",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateOnly>(
                name: "paid_on",
                table: "student_payments",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "student_payments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "student_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "student_payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "receipt_number",
                table: "student_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "student_payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "student_payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                table: "student_payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "voided_at_utc",
                table: "student_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by_user_id",
                table: "student_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "paid_on",
                table: "faculty_payouts",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at_utc",
                table: "faculty_payouts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "faculty_payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "faculty_payouts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "faculty_payouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                table: "faculty_payouts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "voided_at_utc",
                table: "faculty_payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by_user_id",
                table: "faculty_payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "faculty",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "faculty",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "faculty",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateOnly>(
                name: "spent_on",
                table: "expenses",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "expenses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "expenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                table: "expenses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "voided_at_utc",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by_user_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "courses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "courses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "courses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                table: "app_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at_utc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lockout_end_utc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "app_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "app_users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "app_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "test_date",
                table: "academy_tests",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "academy_tests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "academy_tests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "academy_tests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_test_results",
                table: "test_results",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_students",
                table: "students",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_student_payments",
                table: "student_payments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_faculty_payouts",
                table: "faculty_payouts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_faculty",
                table: "faculty",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_expenses",
                table: "expenses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_courses",
                table: "courses",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_batches",
                table: "batches",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_app_users",
                table: "app_users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_academy_tests",
                table: "academy_tests",
                column: "id");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_address = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    max_students = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    suspended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fee_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("pk_fee_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_adjustments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fee_adjustments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    current_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_counters",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_counters", x => new { x.tenant_id, x.name });
                    table.ForeignKey(
                        name: "fk_tenant_counters_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    plan_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    provider_order_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(MoveExistingDataIntoDefaultTenant);

            migrationBuilder.CreateIndex(
                name: "ix_test_results_tenant_id_student_id",
                table: "test_results",
                columns: new[] { "tenant_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_id_course_id",
                table: "students",
                columns: new[] { "tenant_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_id_created_at_utc",
                table: "students",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_id_full_name",
                table: "students",
                columns: new[] { "tenant_id", "full_name" });

            migrationBuilder.CreateIndex(
                name: "ix_student_payments_tenant_id_paid_on",
                table: "student_payments",
                columns: new[] { "tenant_id", "paid_on" });

            migrationBuilder.CreateIndex(
                name: "ix_student_payments_tenant_id_receipt_number",
                table: "student_payments",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_payments_tenant_id_student_id",
                table: "student_payments",
                columns: new[] { "tenant_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_payouts_tenant_id_faculty_id",
                table: "faculty_payouts",
                columns: new[] { "tenant_id", "faculty_id" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_payouts_tenant_id_paid_on",
                table: "faculty_payouts",
                columns: new[] { "tenant_id", "paid_on" });

            migrationBuilder.CreateIndex(
                name: "ix_faculty_tenant_id_full_name",
                table: "faculty",
                columns: new[] { "tenant_id", "full_name" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_tenant_id_category",
                table: "expenses",
                columns: new[] { "tenant_id", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_tenant_id_spent_on",
                table: "expenses",
                columns: new[] { "tenant_id", "spent_on" });

            migrationBuilder.CreateIndex(
                name: "ix_courses_tenant_id_name",
                table: "courses",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_users_email",
                table: "app_users",
                column: "email",
                unique: true,
                filter: "tenant_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_app_users_tenant_id_email",
                table: "app_users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_academy_tests_tenant_id_course_id",
                table: "academy_tests",
                columns: new[] { "tenant_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "ix_academy_tests_tenant_id_test_date",
                table: "academy_tests",
                columns: new[] { "tenant_id", "test_date" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_created_at_utc",
                table: "audit_logs",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_status_next_attempt_at_utc",
                table: "email_outbox",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_tenant_id_created_at_utc",
                table: "email_outbox",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_adjustments_student_id",
                table: "fee_adjustments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_adjustments_tenant_id_student_id",
                table: "fee_adjustments",
                columns: new[] { "tenant_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_invoice_number",
                table: "subscription_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_provider_order_id",
                table: "subscription_invoices",
                column: "provider_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_status_due_on",
                table: "subscription_invoices",
                columns: new[] { "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_subscription_id_period_start",
                table: "subscription_invoices",
                columns: new[] { "subscription_id", "period_start" },
                unique: true,
                filter: "status <> 'Void'");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_tenant_id_created_at_utc",
                table: "subscription_invoices",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_current_period_end",
                table: "subscriptions",
                columns: new[] { "status", "current_period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_code",
                table: "tenants",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_academy_tests_courses_course_id",
                table: "academy_tests",
                column: "course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_academy_tests_tenants_tenant_id",
                table: "academy_tests",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_app_users_tenants_tenant_id",
                table: "app_users",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_courses_tenants_tenant_id",
                table: "courses",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_tenants_tenant_id",
                table: "expenses",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_faculty_tenants_tenant_id",
                table: "faculty",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_faculty_payouts_faculty_faculty_id",
                table: "faculty_payouts",
                column: "faculty_id",
                principalTable: "faculty",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_faculty_payouts_tenants_tenant_id",
                table: "faculty_payouts",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_payments_students_student_id",
                table: "student_payments",
                column: "student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_payments_tenants_tenant_id",
                table: "student_payments",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_students_courses_course_id",
                table: "students",
                column: "course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_students_tenants_tenant_id",
                table: "students",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_results_academy_tests_test_id",
                table: "test_results",
                column: "test_id",
                principalTable: "academy_tests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_results_students_student_id",
                table: "test_results",
                column: "student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_test_results_tenants_tenant_id",
                table: "test_results",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_academy_tests_courses_course_id",
                table: "academy_tests");

            migrationBuilder.DropForeignKey(
                name: "fk_academy_tests_tenants_tenant_id",
                table: "academy_tests");

            migrationBuilder.DropForeignKey(
                name: "fk_app_users_tenants_tenant_id",
                table: "app_users");

            migrationBuilder.DropForeignKey(
                name: "fk_courses_tenants_tenant_id",
                table: "courses");

            migrationBuilder.DropForeignKey(
                name: "fk_expenses_tenants_tenant_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_faculty_tenants_tenant_id",
                table: "faculty");

            migrationBuilder.DropForeignKey(
                name: "fk_faculty_payouts_faculty_faculty_id",
                table: "faculty_payouts");

            migrationBuilder.DropForeignKey(
                name: "fk_faculty_payouts_tenants_tenant_id",
                table: "faculty_payouts");

            migrationBuilder.DropForeignKey(
                name: "fk_student_payments_students_student_id",
                table: "student_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_student_payments_tenants_tenant_id",
                table: "student_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_students_courses_course_id",
                table: "students");

            migrationBuilder.DropForeignKey(
                name: "fk_students_tenants_tenant_id",
                table: "students");

            migrationBuilder.DropForeignKey(
                name: "fk_test_results_academy_tests_test_id",
                table: "test_results");

            migrationBuilder.DropForeignKey(
                name: "fk_test_results_students_student_id",
                table: "test_results");

            migrationBuilder.DropForeignKey(
                name: "fk_test_results_tenants_tenant_id",
                table: "test_results");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "email_outbox");

            migrationBuilder.DropTable(
                name: "fee_adjustments");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "subscription_invoices");

            migrationBuilder.DropTable(
                name: "tenant_counters");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropPrimaryKey(
                name: "pk_test_results",
                table: "test_results");

            migrationBuilder.DropIndex(
                name: "ix_test_results_tenant_id_student_id",
                table: "test_results");

            migrationBuilder.DropPrimaryKey(
                name: "pk_students",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_students_tenant_id_course_id",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_students_tenant_id_created_at_utc",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_students_tenant_id_full_name",
                table: "students");

            migrationBuilder.DropPrimaryKey(
                name: "pk_student_payments",
                table: "student_payments");

            migrationBuilder.DropIndex(
                name: "ix_student_payments_tenant_id_paid_on",
                table: "student_payments");

            migrationBuilder.DropIndex(
                name: "ix_student_payments_tenant_id_receipt_number",
                table: "student_payments");

            migrationBuilder.DropIndex(
                name: "ix_student_payments_tenant_id_student_id",
                table: "student_payments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_faculty_payouts",
                table: "faculty_payouts");

            migrationBuilder.DropIndex(
                name: "ix_faculty_payouts_tenant_id_faculty_id",
                table: "faculty_payouts");

            migrationBuilder.DropIndex(
                name: "ix_faculty_payouts_tenant_id_paid_on",
                table: "faculty_payouts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_faculty",
                table: "faculty");

            migrationBuilder.DropIndex(
                name: "ix_faculty_tenant_id_full_name",
                table: "faculty");

            migrationBuilder.DropPrimaryKey(
                name: "pk_expenses",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_tenant_id_category",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_tenant_id_spent_on",
                table: "expenses");

            migrationBuilder.DropPrimaryKey(
                name: "pk_courses",
                table: "courses");

            migrationBuilder.DropIndex(
                name: "ix_courses_tenant_id_name",
                table: "courses");

            migrationBuilder.DropPrimaryKey(
                name: "pk_batches",
                table: "batches");

            migrationBuilder.DropPrimaryKey(
                name: "pk_app_users",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "ix_app_users_email",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "ix_app_users_tenant_id_email",
                table: "app_users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_academy_tests",
                table: "academy_tests");

            migrationBuilder.DropIndex(
                name: "ix_academy_tests_tenant_id_course_id",
                table: "academy_tests");

            migrationBuilder.DropIndex(
                name: "ix_academy_tests_tenant_id_test_date",
                table: "academy_tests");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "test_results");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "receipt_number",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "void_reason",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "voided_at_utc",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "voided_by_user_id",
                table: "student_payments");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "status",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "void_reason",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "voided_at_utc",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "voided_by_user_id",
                table: "faculty_payouts");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "status",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "void_reason",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "voided_at_utc",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "voided_by_user_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "failed_login_count",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "last_login_at_utc",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "lockout_end_utc",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "academy_tests");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "academy_tests");

            migrationBuilder.DropSequence(
                name: "subscription_invoice_number_seq");

            migrationBuilder.RenameColumn(
                name: "remarks",
                table: "test_results",
                newName: "Remarks");

            migrationBuilder.RenameColumn(
                name: "marks",
                table: "test_results",
                newName: "Marks");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "test_results",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "test_id",
                table: "test_results",
                newName: "TestId");

            migrationBuilder.RenameColumn(
                name: "student_id",
                table: "test_results",
                newName: "StudentId");

            migrationBuilder.RenameIndex(
                name: "ix_test_results_test_id_student_id",
                table: "test_results",
                newName: "IX_test_results_TestId_StudentId");

            migrationBuilder.RenameIndex(
                name: "ix_test_results_student_id",
                table: "test_results",
                newName: "IX_test_results_StudentId");

            migrationBuilder.RenameColumn(
                name: "remarks",
                table: "students",
                newName: "Remarks");

            migrationBuilder.RenameColumn(
                name: "phone",
                table: "students",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "students",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "active",
                table: "students",
                newName: "Active");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "students",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "full_name",
                table: "students",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "fee_amount",
                table: "students",
                newName: "FeeAmount");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "students",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "course_id",
                table: "students",
                newName: "CourseId");

            migrationBuilder.RenameColumn(
                name: "admission_date",
                table: "students",
                newName: "AdmissionDate");

            migrationBuilder.RenameIndex(
                name: "ix_students_course_id",
                table: "students",
                newName: "IX_students_CourseId");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "student_payments",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "method",
                table: "student_payments",
                newName: "Method");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "student_payments",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "student_payments",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "student_id",
                table: "student_payments",
                newName: "StudentId");

            migrationBuilder.RenameColumn(
                name: "paid_on",
                table: "student_payments",
                newName: "PaidOn");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "student_payments",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_student_payments_student_id",
                table: "student_payments",
                newName: "IX_student_payments_StudentId");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "faculty_payouts",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "method",
                table: "faculty_payouts",
                newName: "Method");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "faculty_payouts",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "faculty_payouts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "paid_on",
                table: "faculty_payouts",
                newName: "PaidOn");

            migrationBuilder.RenameColumn(
                name: "faculty_id",
                table: "faculty_payouts",
                newName: "FacultyId");

            migrationBuilder.RenameIndex(
                name: "ix_faculty_payouts_faculty_id",
                table: "faculty_payouts",
                newName: "IX_faculty_payouts_FacultyId");

            migrationBuilder.RenameColumn(
                name: "specialization",
                table: "faculty",
                newName: "Specialization");

            migrationBuilder.RenameColumn(
                name: "phone",
                table: "faculty",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "faculty",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "active",
                table: "faculty",
                newName: "Active");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "faculty",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "full_name",
                table: "faculty",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "faculty",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "agreed_payout",
                table: "faculty",
                newName: "AgreedPayout");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "expenses",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "expenses",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "method",
                table: "expenses",
                newName: "Method");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "expenses",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "expenses",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "expenses",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "spent_on",
                table: "expenses",
                newName: "SpentOn");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "expenses",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "courses",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "fee",
                table: "courses",
                newName: "Fee");

            migrationBuilder.RenameColumn(
                name: "active",
                table: "courses",
                newName: "Active");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "courses",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "courses",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "app_users",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "app_users",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "active",
                table: "app_users",
                newName: "Active");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "app_users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                table: "app_users",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "display_name",
                table: "app_users",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "app_users",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "academy_tests",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "subject",
                table: "academy_tests",
                newName: "Subject");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "academy_tests",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "academy_tests",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "test_date",
                table: "academy_tests",
                newName: "TestDate");

            migrationBuilder.RenameColumn(
                name: "max_marks",
                table: "academy_tests",
                newName: "MaxMarks");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "academy_tests",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "course_id",
                table: "academy_tests",
                newName: "CourseId");

            migrationBuilder.RenameIndex(
                name: "ix_academy_tests_course_id",
                table: "academy_tests",
                newName: "IX_academy_tests_CourseId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "students",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AdmissionDate",
                table: "students",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidOn",
                table: "student_payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "student_payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaidOn",
                table: "faculty_payouts",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "faculty",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "SpentOn",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "courses",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "TestDate",
                table: "academy_tests",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "academy_tests",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_test_results",
                table: "test_results",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_students",
                table: "students",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_student_payments",
                table: "student_payments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_faculty_payouts",
                table: "faculty_payouts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_faculty",
                table: "faculty",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_expenses",
                table: "expenses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_courses",
                table: "courses",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_batches",
                table: "batches",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_users",
                table: "app_users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_academy_tests",
                table: "academy_tests",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_students_Email",
                table: "students",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_courses_Name",
                table: "courses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_users_Email",
                table: "app_users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_academy_tests_courses_CourseId",
                table: "academy_tests",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_faculty_payouts_faculty_FacultyId",
                table: "faculty_payouts",
                column: "FacultyId",
                principalTable: "faculty",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_student_payments_students_StudentId",
                table: "student_payments",
                column: "StudentId",
                principalTable: "students",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_students_courses_CourseId",
                table: "students",
                column: "CourseId",
                principalTable: "courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_test_results_academy_tests_TestId",
                table: "test_results",
                column: "TestId",
                principalTable: "academy_tests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_test_results_students_StudentId",
                table: "test_results",
                column: "StudentId",
                principalTable: "students",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
