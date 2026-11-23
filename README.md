# Ojas

Multi-tenant finance management for academies, sold on subscription.

- **You (platform owner)** create academies, choose their plan, and issue each one a Super Admin login.
- **Each academy** signs in with its own academy code, manages its own staff logins, and sees only its own data.
- **Free, Basic and Pro plans**: new academies start on the Free plan (25 students, 2 faculty, 1 login, 50 emailed receipts a month) with no trial and upgrade when they need more. The owner edits limits and creates plans on the Plans page. A paid plan that is not renewed falls back to Free instead of locking the academy out; limits never delete anything.

Stack: Angular 21 · ASP.NET Core 10 · PostgreSQL · one Docker container (Render).

## Roles

| Role | Belongs to | Can do |
|---|---|---|
| Owner | Platform | Create/suspend academies, plans, subscriptions, invoices, each academy's logo and tagline. Has no access to academy data screens. |
| Super Admin | One academy | Everything in the academy, including the subscription, academic years, logo/tagline, the daily activity report and other Super Admins. |
| Admin | One academy | Everything except the subscription, academic years, logo/tagline and the daily activity report. Cannot manage Super Admins. |
| Accountant | One academy | Fees, refunds, discounts, expenses, payouts, faculty teaching time, reports. |
| Staff | One academy | Students, online admissions, fee collection, test marks. Sees no expenses, payouts or totals. |
| Faculty | One faculty member | Their own portal only: clocks in and out of classes, sees their own sessions and pay. Created from the Faculty screen. |
| Student | One student | Their own portal only: fees, receipts, subjects and test results. Created from the student record. |

Faculty and student logins do not count towards the plan's user limit. Every new login is emailed its sign-in details and
starting password, and must choose its own password before it can do anything else. Passwords need at least 5 characters; five
wrong attempts lock an account for 15 minutes.

Endpoints require a *permission*; roles map to permissions in `server/Academy.Api/Infrastructure/Auth/Permissions.cs`.

## What an academy gets

Students and courses · fee payments with numbered, printable receipts · discounts, surcharges and refunds ·
faculty and payouts · expenses · tests and report cards · dashboard · reports with CSV export ·
email fee reminders · user management · activity log · online admission form with QR code · own logo and tagline ·
faculty portal with QR / location clock-in · student portal · day/week/month reports and a daily activity report in Excel.

Money entries are never edited or deleted. A mistake is **voided** with a reason and re-entered, so totals always reconcile.

### Academy logo and tagline

The Super Admin (**Academy profile**) or the owner (academy page in the console) uploads a logo and sets a tagline.
The logo appears beside the academy name for every user, on the browser tab, on the sign-in page once the academy code
is typed, and on the admission form. Images are resized in the browser and stored in the database (Render's disk is not
persistent); only PNG, JPEG and WebP are accepted, checked from the file's own bytes.

### Online admissions

**Admissions** shows the academy's public form link (`/apply/<academy code>`) and a QR code to print or download.
Anyone can apply without signing in; applications wait in a queue until staff **approve** them (choosing course, fee and
date — the student is created in the selected academic year) or **reject** them with a reason. The form can be closed at
any time. Spam protection: per-IP rate limit, a hidden honeypot field, duplicate check by phone and course, and a cap
of 1,000 pending applications per academy.

### Subject master

**Subjects** holds the subjects the academy teaches (name, optional short code, active/inactive). Courses list the
subjects they teach and faculty list the subjects they teach (both picked by tick boxes). A test is set in one of its
course's subjects, chosen from a dropdown, so report cards group results by real subjects. Rules: names are unique per
academy ignoring case; a subject that has tests in a course stays linked to that course; inactive subjects cannot be
newly linked but keep their history; renaming a subject renames it everywhere. Logged teaching time can record which of
the faculty member's subjects the class was in.

Upgrading from free-text subjects is automatic: the migration creates one subject per distinct typed name (ignoring
case and spaces; the most used spelling wins), puts tests with no subject under "General", points each test at its
subject and links each course to the subjects its tests were in.

### Faculty pay: package or hourly

Each faculty member is paid either a **package** (fixed amount, e.g. annual) or **hourly**. For hourly faculty, staff log
each class as whole **minutes** (e.g. 85 min); pay = minutes × hourly rate ÷ 60, rounded to the paisa. The rate is copied
onto each logged session, so changing it later never re-prices past classes. Owed = earned − paid for both kinds, and
payouts cannot exceed it. A logged session that has already been paid for cannot be voided until the payout is voided.

### Faculty portal: clock in and clock out

A faculty member given a **portal login** (Faculty screen → *Give portal login*) signs in with the academy code and lands
on their own portal (`/teacher`), built for a phone. They only tap **Clock in** when a class starts and **Clock out**
when it ends; the server's clock gives the minutes, and a live timer runs for them while the class is on.

- **QR code (the default).** An admin opens **Class check-in**, which shows a QR code that changes every 20 seconds
  (each code works for one minute and is signed with the academy's own secret, so a photo of it is useless later).
  The faculty member scans it with the portal's scanner, or with the phone's own camera app, which opens the portal.
  A class clocked in *and* out by QR is **approved at once**.
- **Location (when no admin is free).** The Super Admin sets the academy's location and an allowed distance on
  **Academy profile**. Faculty can then clock in or out by location within that distance; those classes wait in
  **Waiting for approval** on the Faculty screen, which shows how each end was proved (QR, or location and distance).
- Under a minute is treated as a mistaken tap and discarded.
- **Longest normal class** (Academy profile, default 3 hours): a longer class always goes for approval, even by QR, and
  the faculty member is emailed once to clock out. Admins see a "probably forgot to clock out" alert.
- **Late clock-out:** the faculty member can enter when the class really ended; an admin **stopping** a class can too.
  Either way it goes for approval, with the tap time and the entered time both shown.
- **Midnight:** a class still running is closed automatically ("needs an end time"), pays nothing until the faculty
  member or an admin enters when it ended, and no longer blocks the next clock-in.
- **Forgot to clock in:** the faculty member enters the date, start and end times and a reason (up to 31 days back).
  It is marked as a manual request and always needs approval; it cannot overlap another class.
- **Classes running now**, with live timers, is on the dashboard and the Class check-in page for Admins and Super Admins.
- Package faculty clock in and out too; their classes are recorded as attendance at no extra pay, approved
  automatically unless the academy turns that off. Their portal shows time taught, days present and package balance.
- **Faculty → Attendance** shows each faculty member's days present, classes and time for a month, with an Excel download.
- The portal also has the month's figures, sessions by month (All / Waiting / Approved / Paid), and **My payments**.
  Payouts are not tied to sessions, so a session shows as *Paid* once payouts cover it, oldest first.

### Student portal

A student given a **portal login** (student record → *Give portal login*) signs in with the academy code and sees
(`/student`) their course and year, their subjects and who teaches them, latest test results and the full report card.
**Nothing about money**: the agreed fee and any discount stay with the office. Instead, whenever a payment or refund is
recorded (or voided), the student is **emailed a receipt** showing that payment, paid to date and balance due, sent as
the academy. Students without an email on record get no email; the office receipt can still be printed.

### Fees: settling the last instalment

**Settle balance** on a student's record records the final payment (if any) and writes off the rest, with a reason.
The student becomes **Settled**: nothing is due, reminders stop, and the student is not told the written-off amount.
Write-offs are reported separately from discounts (reports, Daily report, Excel) and can be voided if made by mistake.

### Promote to next year

**Students → Promote to next year**: pick the year (and course), tick the continuing students and set each one's new
course and fee. Each gets a new record in the next year, linked to last year's; the old record becomes inactive but keeps
its payments, and anything still unpaid there is shown on the new record and stays collectable. The student's portal
login moves to the new record and their report card covers both years. Others can be marked inactive in the same step.

### Reports and the daily activity report

**Reports** groups money in and out **day by day, week by week (Monday–Sunday) or month by month** within the chosen
dates, and every download is an Excel workbook (CSV is still available with `?format=csv`). The summary workbook follows
the grouping picked on screen.

**Daily report** (Super Admin) lists everything that happened in a date range — admissions, online applications, fee
payments, refunds, discounts and surcharges, expenses, faculty payouts, teaching time and tests — with who entered it,
plus totals by day, week or month. *Download Excel* gives the same grouping on one sheet and every entry, with filters,
on another. Voided money and rejected or withdrawn teaching time are listed but left out of totals. Times are Indian time.

### Expense proof

Each expense can carry up to five bills, invoices or payment screenshots (photos or PDFs, 5 MB each), attached when it
is recorded or later. Phone photos are shrunk in the browser before upload. Files are stored in the database
(`expense_proofs`), because the server's own disk does not survive a restart on Render; the type is checked from the
file's bytes, and only images and PDFs are accepted. The Expenses list shows 📎 with the number of files; the Excel
export has a *Proof attached* column. A voided expense keeps its proof and it can no longer change. The Super Admin can
set **Proof rule → required above ₹X**: larger expenses then cannot be saved without a bill, and their last bill cannot be
removed. Removing a bill shows as a flag on the activity dashboard.

### Activity log dashboard

**Activity log → Dashboard** (Super Admin and Admin) turns the log into numbers and charts for a chosen period: total
actions against the previous period, active users, money edits, deletions, after-hours actions and failed sign-ins;
activity over time (created / changed / deleted, day, week or month); a busiest-hours heatmap; what was worked on; and
activity by user. **Needs attention** flags actions worth a second look:

| Flag | When |
|---|---|
| 🔴 Red | A payment or refund cancelled or its amount changed · a student's agreed fee lowered · a discount or write-off at or above the large-amount alert (default ₹5,000, Super Admin can change it) |
| 🟠 Orange | An expense or faculty payment cancelled · a bill removed from an expense · a smaller discount given by someone other than the Super Admin · a smaller write-off · 5+ deletions or cancellations within 10 minutes · staff working between 10 PM and 6 AM · a sign-in from a network the account has not used before |
| 🟡 Yellow | 3+ wrong passwords for an account in a day (orange at 10+) · a faculty class's time edited · a typed-in or corrected class approved |

The Super Admin marks a flag **reviewed** to clear it (it can be reopened). Flags are worked out from the log each time;
only the reviewed marks are stored. Wrong-password attempts are now logged for this. **History** (on a student, a
faculty member, an expense, a flag or any row of the full log) shows that record's changes newest first, including its
payments and discounts, bills, or classes and payouts.

### Owner analytics

**Platform → Analytics** (owner only) has five tabs. It shows counts, times and totals per academy, never an academy's
own records.

| Tab | What it shows |
|---|---|
| Business growth | Monthly recurring revenue, revenue earned per month (each invoice spread over the months it pays for) and collected, new / upgrade / downgrade / lost revenue, academies by state, trial conversion and days to pay, revenue by plan, renewals in the next 30 days, overdue invoices, academies at 80%+ of a plan limit, academies lost with the reason |
| Academy health | A 0–100 score per academy (days active in the last 14, fees recorded, students added, features used), 🟢 active / 🟡 slowing / 🔴 going quiet (10+ idle days), last activity overall and per role, the set-up checklist and where academies get stuck, platform totals |
| Feature usage | Each feature by use, academies using it (adoption), speed, phone share and main role, an 8-week trend, features nobody used, what each role does, faculty and student portal adoption, mobile vs desktop |
| Peak hours & speed | Heatmap by day and hour (Indian time, also per academy), busiest hour, the quietest three hours to deploy in, busiest minute, most people online in an hour, typical and slowest-5% response times, slowest features, errors by feature, each academy's share of load and late-night use, background work (emails, invoices, auto-closed classes), deployment advice |
| Capacity & cost | Database size against the plan limit, growth per month and when it will be full, bill files, the biggest tables, emails today against the daily limit, and per academy: storage, emails, load, share of hosting cost, revenue and margin |

**How usage is tracked.** A middleware times every `/api` request and counts it in memory by academy, feature (named
from the route), role, device and hour; once a minute the counts are added to `usage_hours`, the accounts seen to
`usage_user_hours` and the busiest minute to `usage_peak_hours`. Hours follow Indian time. Rows hold counts and timings
only, no user names or records. Session checks and sign-ins count towards load but not as features. The database size
is recorded once a day (`database_size_days`). Hosting cost, the database limit and the daily email limit are set
under **Hosting cost and limits** (defaults: 1 GB, 100 emails a day). Each academy's cost share is half by its requests
and half by its storage. Usage figures start from the day this is deployed; revenue and health use existing data, so
they cover the past too.

## Academic years

Each academy has its own year master (Super Admin → **Academic years**). A new academy starts with an April–March year;
the Super Admin can change the dates of the current or a future year and add the next one. Once a year's last day has
passed it is locked. A switch at the top of every screen selects the year; the current year is chosen by default.

| Follows the selected year | Not tied to a year |
|---|---|
| Students (by the year they were admitted) and their fee status | Courses (catalogue; counts shown are for the selected year) and subjects |
| Payments, refunds, expenses, faculty payouts and teaching time (by date) | Faculty records, packages and hourly rates, what faculty are owed, the faculty portal (by month) and its approval queue |
| | Online admission applications (approved ones join the selected year) |
| Tests and report cards | Users, activity log, subscription |
| Dashboard and reports | |

Dues from earlier years stay visible on the dashboard and can still be collected. New admissions are blocked in a year that has ended.

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

- **Email (`Email__*`)** – password-reset links, welcome emails, fee reminders, invoices, clock-out reminders.
  Recommended: set `Email__ResendApiKey` (or `Email__BrevoApiKey`) and `Email__FromAddress` (an address on the domain
  you verified with that service); mail then goes through its API over HTTPS, which works on hosts that block mail ports. Each academy's mail shows the academy's
  name as sender and replies go to the academy's own address (Academy profile → Emails); the mail itself always leaves
  from the platform's verified address. SMTP (`Email__Host` and friends) still works instead. Until configured, messages are written to the
  server log and marked *failed – not configured*. Any SMTP service works, for example Brevo, Zoho ZeptoMail, Amazon SES,
  or a Google Workspace / Gmail account with an app password (fine for testing, low daily limits). Use a sender address on
  a domain you own and add the provider's SPF and DKIM records to that domain, or messages will land in spam. After
  setting the variables and restarting, use **Send test email** on the owner Overview page.
- **Razorpay (`Razorpay__*`)** – online payment of subscription invoices. Until configured, academies pay you directly and you click *Mark paid*.
  Set the webhook URL in Razorpay to `https://<your-site>/api/billing/webhooks/razorpay` (events `payment.captured`, `order.paid`).

## Run locally

Needs .NET SDK 10, Node 20.19+/22.12+, PostgreSQL.

```bash
# API (applies migrations and creates the owner on first start)
export ConnectionStrings__AcademyDb='Host=localhost;Database=academyflow;Username=postgres;Password=...'
export OWNER_EMAIL='you@example.com' OWNER_PASSWORD='at-least-5-characters'
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
