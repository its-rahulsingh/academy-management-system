# Prototype video → AcademyFlow implementation

This implementation follows the user-supplied 140-second prototype walkthrough.

| Approximate video timestamp | Shown in video | Implemented in source |
| --- | --- | --- |
| 0–36s | Marketing landing, features, platform, pricing, roadmap | Responsive landing page, demo illustration, prototypes pricing **labeled as illustrative** and future roadmap, sign-in CTA (no public signup or payment checkout) |
| 36–40s | Welcome/login form with Google | Email/password only; private administrator-created user accounts; no Google login |
| 40–56s | Academy Dashboard with metrics, chart, insights, quick actions | Dashboard KPIs derived from Neon records, course-based collection graph, insights, recent transactions and shortcuts |
| 56–96s | Students list, filters, admission form (course, fee, date, remarks) | Full student create/edit, course selection, fees, admission date, status and search/filter |
| 96–104s | Faculty table and add faculty | Faculty directory, agreed payout, amounts paid/due, faculty payout recording and history |
| 104–112s | Courses cards and progress | Course creation, active/enrollment/revenue cards |
| 112–120s | Payments list and method filter | Payments CRUD **create/list only** (no delete to protect historical ledger), method filter, balances and partial collection checks |
| 120–128s | Expenses list and add expense | Expense recording, method, category, date, notes and KPI summary |
| 128–140s | Tests, report cards, add test | Course tests, score recording, report list and per-student average |

## Specific intentional deviations

- The prototype numbers, names, and colorful revenue spikes were examples. The new database starts without fabricated financial transactions, and the chart renders **live course receipts** rather than fabricated monthly data.
- "Net profit" in the video is renamed **Net Cash Balance** to avoid falsely representing a cash-based calculation as accrual-accounting profit.
- Only Admin role provisions users. The back-office can never register accounts anonymously.
- Student fees and courses become locked once payments exist so finance totals cannot be silently reallocated after collection.
- The existing Render Dockerfile, Neon connection-string key, frontend output folder and Render Blueprint are preserved, not replaced.
- Subscription purchasing, marketing lead collection, payroll automation, multi-academy tenancy and messaging are future scope, not implied by the promotional prototype.
