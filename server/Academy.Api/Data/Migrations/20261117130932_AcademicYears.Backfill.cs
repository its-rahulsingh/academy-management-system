namespace Academy.Api.Data.Migrations
{
    /// <summary>Hand-written data step, kept apart from the generated schema operations.</summary>
    public partial class AcademicYears
    {
        /// <summary>
        /// Gives every existing academy one academic year and puts its existing students in it.
        /// The year runs April to March and reaches back far enough to cover the academy's oldest record,
        /// so nothing already entered falls outside it. It ends after today, so it is the current year and
        /// its Super Admin can still correct the dates. Runs before the foreign key is created.
        /// </summary>
        private const string CreateFirstYearPerAcademy = """
            WITH bounds AS (
                SELECT t.id AS tenant_id,
                       LEAST(
                           CURRENT_DATE,
                           (SELECT MIN(s.admission_date) FROM students s WHERE s.tenant_id = t.id),
                           (SELECT MIN(p.paid_on) FROM student_payments p WHERE p.tenant_id = t.id),
                           (SELECT MIN(e.spent_on) FROM expenses e WHERE e.tenant_id = t.id),
                           (SELECT MIN(f.paid_on) FROM faculty_payouts f WHERE f.tenant_id = t.id),
                           (SELECT MIN(x.test_date) FROM academy_tests x WHERE x.tenant_id = t.id)
                       ) AS earliest
                FROM tenants t
            ),
            years AS (
                SELECT tenant_id,
                       CAST(EXTRACT(YEAR FROM earliest) AS int) - CASE WHEN EXTRACT(MONTH FROM earliest) < 4 THEN 1 ELSE 0 END AS first_year,
                       CAST(EXTRACT(YEAR FROM CURRENT_DATE) AS int) - CASE WHEN EXTRACT(MONTH FROM CURRENT_DATE) < 4 THEN 1 ELSE 0 END AS this_year
                FROM bounds
            )
            INSERT INTO academic_years (id, tenant_id, name, start_date, end_date, created_at_utc)
            SELECT gen_random_uuid(),
                   tenant_id,
                   first_year || '-' || LPAD(CAST((this_year + 1) % 100 AS text), 2, '0'),
                   make_date(first_year, 4, 1),
                   make_date(this_year + 1, 3, 31),
                   now()
            FROM years;

            UPDATE students s
            SET academic_year_id = y.id
            FROM academic_years y
            WHERE y.tenant_id = s.tenant_id;
            """;
    }
}
