using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    public partial class SubjectMaster
    {
        /// <summary>
        /// Turns the free-text subjects typed on existing tests into the subject master, without losing any:
        /// 1. one subject per academy per distinct name, ignoring case and surrounding spaces
        ///    (the most used spelling wins); tests with no subject go under "General";
        /// 2. each test points at its subject;
        /// 3. each course is linked to the subjects its tests were in.
        /// </summary>
        private static void CarryOverTypedSubjects(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                with typed as (
                    select tenant_id, left(coalesce(nullif(btrim(subject), ''), 'General'), 100) as name
                    from academy_tests
                ),
                spellings as (
                    select tenant_id, lower(name) as k, name, count(*) as uses
                    from typed
                    group by tenant_id, lower(name), name
                ),
                ranked as (
                    -- Most used spelling first; on a tie prefer "Physics" over "physics" or "PHYSICS".
                    select tenant_id, name,
                           row_number() over (partition by tenant_id, k order by uses desc, (name = initcap(name)) desc, name) as rn
                    from spellings
                )
                insert into subjects (id, tenant_id, name, code, active, created_at_utc)
                select gen_random_uuid(), tenant_id, name, '', true, now()
                from ranked
                where rn = 1;
                """);

            migrationBuilder.Sql("""
                update academy_tests t
                set subject_id = s.id
                from subjects s
                where s.tenant_id = t.tenant_id
                  and lower(s.name) = lower(left(coalesce(nullif(btrim(t.subject), ''), 'General'), 100));
                """);

            migrationBuilder.Sql("""
                insert into course_subjects (tenant_id, course_id, subject_id)
                select distinct tenant_id, course_id, subject_id
                from academy_tests;
                """);
        }
    }
}
