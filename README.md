# AcademyFlow

Multi-tenant finance management for academies, sold on subscription.

- **You (platform owner)** create academies, choose their plan, and issue each one a Super Admin login.
- **Each academy** signs in with its own academy code, manages its own staff logins, and sees only its own data.
- **Access follows the subscription**: trial, renewal invoices, grace period, lock-out when unpaid.

Stack: Angular 21 · ASP.NET Core 10 · PostgreSQL · one Docker container (Render).

## Roles

| Role | Belongs to | Can do |
|---|---|---|
| Owner | Platform | Create/suspend academies, plans, subscriptions, invoices. Has no access to academy data screens. |
| Super Admin | One academy | Everything in the academy, including the subscription and other Super Admins. |
| Admin | One academy | Everything except the subscription. Cannot manage Super Admins. |
| Accountant | One academy | Fees, refunds, discounts, expenses, payouts, reports. |
| Staff | One academy | Students, fee collection, test marks. Sees no expenses, payouts or totals. |

Endpoints require a *permission*; roles map to permissions in `server/Academy.Api/Infrastructure/Auth/Permissions.cs`.

## What an academy gets

Students and courses · fee payments with numbered, printable receipts · discounts, surcharges and refunds ·
faculty and payouts · expenses · tests and report cards · dashboard · reports with CSV export ·
email fee reminders · user management · activity log.

Money entries are never edited or deleted. A mistake is **voided** with a reason and re-entered, so totals always reconcile.

## Project layout

```
client/src/app/
  core/        session, API client, guards, shared state helpers, API types
  shared/      dialog, pager, pipes
  features/    public/ (landing, sign-in)  shell/  academy/  platform/ (owner console)
server/Academy.Api/
  Domain/          entities
  Data/            DbContext (tenant filter + write guard + audit), table mappings, migrations
  Infrastructure/  auth, tenancy, billing, email, web plumbing, Startup.cs
  Features/        one file of endpoints per module
server/Academy.Api.Tests/   integration and unit tests
```

## How tenant isolation works

One database, shared tables, a `tenant_id` on every academy-owned row. It is enforced centrally in
`AcademyDbContext`, not endpoint by endpoint:

1. **Reads**: a global query filter adds `tenant_id = <caller's academy>` to every query.
2. **Writes**: saving stamps new rows with the caller's academy and throws on any cross-academy write.
3. **Gate**: every academy endpoint sits behind `TenantAccessFilter` (signed in, has an academy, subscription in good standing; otherwise HTTP 402).

Owner-console code opts out explicitly with `IgnoreQueryFilters()` and names the academy it acts on.
`TenantIsolationTests` proves one academy cannot read or change another's records.

## Configuration

See `.env.example`. Required: `ConnectionStrings__AcademyDb`, `OWNER_EMAIL`, `OWNER_PASSWORD`
(the owner account is created once, at first start; remove `OWNER_PASSWORD` afterwards).

Optional, each works without the other:

- **Email (`Email__*`)** – password-reset links, welcome emails, fee reminders, invoices. Until configured, emails are logged, not sent.
- **Razorpay (`Razorpay__*`)** – online payment of subscription invoices. Until configured, academies pay you directly and you click *Mark paid*.
  Set the webhook URL in Razorpay to `https://<your-site>/api/billing/webhooks/razorpay` (events `payment.captured`, `order.paid`).

## Run locally

Needs .NET SDK 10, Node 20.19+/22.12+, PostgreSQL.

```bash
# API (applies migrations and creates the owner on first start)
export ConnectionStrings__AcademyDb='Host=localhost;Database=academyflow;Username=postgres;Password=...'
export OWNER_EMAIL='you@example.com' OWNER_PASSWORD='at-least-12-characters'
dotnet run --project server/Academy.Api --urls http://localhost:5180

# Frontend (proxies /api to :5180)
cd client && npm ci && npm start     # http://localhost:4200
```

Sign in as the owner with the **academy code left blank**. Create an academy; its staff sign in with that academy's code.

## Tests

```bash
export ACADEMY_TEST_POSTGRES='Host=localhost;Port=5432;Username=postgres;Password=...'
dotnet test server/Academy.Api.Tests
```

Each run creates and drops its own database on that server.

## Deploy (Render)

`Dockerfile` and `render.yaml` build one container serving the API and the Angular app. Set the variables from
`.env.example` in Render → Environment. Migrations run at startup; **back up the database before deploying an upgrade**.

Upgrading a database from the earlier single-academy version: existing data is moved into one academy with code
`default`, and its Admin users become Super Admins. Sign in as owner and assign that academy a plan to give it access.

## Known limits

- Session and subscription checks are cached per server for 30 seconds, so a suspension or disabled user can take up to 30 seconds to apply on another server instance.
- Razorpay and SMTP code paths are covered by unit tests for signatures only; they have not been exercised against live accounts.
- Render's free plan sleeps when idle and is not suitable for paying customers.
- GST on subscription invoices is not calculated.
