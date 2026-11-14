# AcademyFlow local PostgreSQL — Windows CMD startup (migration fix)

## What this patch fixes

- The existing `AcademyModules` migration creates nine additional application tables, but the original `AcademyDbContextModelSnapshot.cs` described only `batches`. EF Core consequently threw `PendingModelChangesWarning` while calling `MigrateAsync()`.
- The EF model snapshot now describes all 10 entities, with a matching target model for the academy migration. We did not remove migrations, suppress warnings, or alter the original migration / Render deployment settings.
- Local `http://localhost:4200` testing now works with authentication and anti-forgery cookies if the backend is run with `ASPNETCORE_ENVIRONMENT=Development`. Production retains Secure/HTTPS-only cookies.

## CMD: Terminal 1 (backend)

Open Command Prompt (cmd.exe), not PowerShell, and go to the **newly extracted fixed ZIP** root:

```cmd
cd /d C:\Users\Admin\Downloads\AcademyFlow_Combined_Fixed
set "ASPNETCORE_ENVIRONMENT=Development"
set "ConnectionStrings__AcademyDb=Host=localhost;Port=5432;Database=academyflow_local;Username=postgres;Password=YOUR_POSTGRES_PASSWORD;SSL Mode=Disable"
set "ADMIN_EMAIL=admin@academy.com"
set "ADMIN_PASSWORD=REPLACE_WITH_UNIQUE_12_PLUS_CHARACTER_PASSWORD"
dotnet restore server\Academy.Api\Academy.Api.csproj
dotnet run --project server\Academy.Api\Academy.Api.csproj --urls http://localhost:5180
```

Use your real local PostgreSQL password and a strong admin password (12+ characters). The initial Admin is created only if no Admin account exists already. All `set` variables apply only to this CMD session.

Open http://localhost:5180/api/health and confirm healthy. Check the console for migration or database errors.

## CMD: Terminal 2 (frontend)

```cmd
cd /d C:\Users\Admin\Downloads\AcademyFlow_Combined_Fixed\client
npm ci
npm start
```

Open http://localhost:4200 and log in using your `ADMIN_EMAIL` and `ADMIN_PASSWORD`.

## If it still fails

Do **not** ignore or disable `PendingModelChangesWarning`, or call `EnsureCreated()` in place of migrations. Copy the full new exception (first error and the stack trace). Verify PostgreSQL is reachable and the database `academyflow_local` exists.

To inspect the migration history after starting successfully (psql):

```cmd
psql -h localhost -U postgres -d academyflow_local -c "SELECT * FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"
```

You should see `20261018095031_InitialAcademySchema` and `20261018120000_AcademyModules` for a newly migrated database. If the history query says the table is missing, inspect the actual startup error; don't manually mark migrations as applied.

## No Render changes

The original Dockerfile, `render.yaml`, Angular proxy/build settings, .NET project file, API appsettings, and first migration remain untouched. The new `ASPNETCORE_ENVIRONMENT=Development` CMD setting is for **local CMD only**, not Render. Your Neon database remains unaffected if the local connection string above is correctly used.

## Verification limits

Static source checks pass. A full `dotnet build`, EF model-difference validation, PostgreSQL migration execution, and browser login test could not run in the packaging environment (no .NET SDK or PostgreSQL configured); validate on your machine before deployment.
