using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FreemiumPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "custom_branding",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "max_faculty",
                table: "plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_receipt_emails_per_month",
                table: "plans",
                type: "integer",
                nullable: true);

            // The freemium price list. Existing plans keep their branding (the column defaults to true above).
            migrationBuilder.Sql("""
                INSERT INTO plans (id, code, name, description, price, currency, interval, max_students, max_users, max_faculty,
                                   max_receipt_emails_per_month, custom_branding, is_active, created_at_utc)
                SELECT gen_random_uuid(), v.code, v.name, v.description, v.price, 'INR', v.interval, v.ms, v.mu, v.mf, v.me, v.cb, true, now()
                FROM (VALUES
                    ('free',         'Free',           'For small classes getting started. Free forever.',           0::numeric,    'Monthly', 25,  1, 2,  50::int, false),
                    ('basic',        'Basic',          'For growing classes.',                                        199::numeric,  'Monthly', 75,  2, 5,  NULL,    true),
                    ('basic-yearly', 'Basic (yearly)', 'Basic, paid once a year: about two months free.',             1999::numeric, 'Yearly',  75,  2, 5,  NULL,    true),
                    ('pro',          'Pro',            'For established academies.',                                  499::numeric,  'Monthly', 250, 5, 15, NULL,    true),
                    ('pro-yearly',   'Pro (yearly)',   'Pro, paid once a year: about two months free.',               4999::numeric, 'Yearly',  250, 5, 15, NULL,    true)
                ) AS v(code, name, description, price, interval, ms, mu, mf, me, cb)
                WHERE NOT EXISTS (SELECT 1 FROM plans p WHERE p.code = v.code);
                """);

            // The launch plans are retired; academies on them move to Free. Their unpaid invoices for the old plans are voided.
            migrationBuilder.Sql("""
                UPDATE subscriptions s
                SET plan_id = f.id, price = 0, interval = 'Monthly', status = 'Active',
                    current_period_start = CURRENT_DATE, current_period_end = CURRENT_DATE, updated_at_utc = now()
                FROM plans f, plans old
                WHERE f.code = 'free' AND old.id = s.plan_id AND old.code IN ('starter', 'growth') AND s.status <> 'Cancelled';

                UPDATE subscription_invoices i
                SET status = 'Void', notes = 'Moved to the Free plan.'
                FROM subscriptions s JOIN plans f ON f.id = s.plan_id AND f.code = 'free'
                WHERE i.subscription_id = s.id AND i.status = 'Open';

                UPDATE plans SET is_active = false WHERE code IN ('starter', 'growth');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_branding",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "max_faculty",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "max_receipt_emails_per_month",
                table: "plans");
        }
    }
}
