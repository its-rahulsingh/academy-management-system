namespace Academy.Api.Data.Migrations
{
    /// <summary>
    /// Hand-written data steps for the single-academy to multi-tenant upgrade. Kept beside the generated
    /// migration so regenerating the schema operations never loses them.
    /// </summary>
    public partial class MultiTenantPlatform
    {
        /// <summary>
        /// The old columns held midnight-UTC timestamps. Converting them to plain dates must be done in UTC,
        /// or a server in another time zone would shift every date by a day.
        /// </summary>
        private const string UseUtcForDateConversion = "SET LOCAL TIME ZONE 'UTC';";

        /// <summary>
        /// Runs after the new columns and tables exist but before the tenant indexes and foreign keys are
        /// created. Any data from the single-academy version is moved into one academy with code "default";
        /// its Admin users become that academy's Super Admins. A fresh database is left untouched.
        /// </summary>
        private const string MoveExistingDataIntoDefaultTenant = """
            UPDATE student_payments SET status = 'Posted', kind = 'Payment' WHERE status = '';
            UPDATE faculty_payouts SET status = 'Posted' WHERE status = '';
            UPDATE expenses SET status = 'Posted' WHERE status = '';
            UPDATE app_users SET security_stamp = md5(random()::text || id::text) WHERE security_stamp = '';

            DO $$
            DECLARE
                legacy_tenant uuid;
            BEGIN
                IF EXISTS (SELECT 1 FROM app_users) OR EXISTS (SELECT 1 FROM courses)
                   OR EXISTS (SELECT 1 FROM faculty) OR EXISTS (SELECT 1 FROM expenses) THEN
                    legacy_tenant := gen_random_uuid();

                    INSERT INTO tenants (id, code, name, status, contact_name, contact_email, contact_phone, created_at_utc)
                    VALUES (legacy_tenant, 'default', 'My Academy', 'Active', '', '', '', now());

                    UPDATE app_users
                    SET tenant_id = legacy_tenant,
                        role = CASE WHEN role = 'Admin' THEN 'SuperAdmin' ELSE role END;

                    UPDATE courses SET tenant_id = legacy_tenant;
                    UPDATE students SET tenant_id = legacy_tenant;
                    UPDATE faculty SET tenant_id = legacy_tenant;
                    UPDATE student_payments SET tenant_id = legacy_tenant;
                    UPDATE faculty_payouts SET tenant_id = legacy_tenant;
                    UPDATE expenses SET tenant_id = legacy_tenant;
                    UPDATE academy_tests SET tenant_id = legacy_tenant;
                    UPDATE test_results SET tenant_id = legacy_tenant;

                    -- Receipt numbers are new: number existing payments in the order they were recorded.
                    UPDATE student_payments p
                    SET receipt_number = numbered.receipt_number
                    FROM (SELECT id, row_number() OVER (ORDER BY created_at_utc, id) AS receipt_number FROM student_payments) numbered
                    WHERE numbered.id = p.id;

                    INSERT INTO tenant_counters (tenant_id, name, next_value)
                    SELECT legacy_tenant, 'receipt', COALESCE(MAX(receipt_number), 0) + 1 FROM student_payments;
                END IF;
            END $$;
            """;
    }
}
