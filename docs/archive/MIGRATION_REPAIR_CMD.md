# Fix EF Core PendingModelChangesWarning (Windows CMD)

The earlier AcademyModules migration and model snapshot were manually authored; EF Core did not generate them, so metadata did not match its model. This package intentionally **does not include** that invalid hand-authored module migration. Instead, run the official EF migration generator locally, once, to create a correct migration and snapshot from the application model. **Do not suppress the warning. Do not run `EnsureCreated`. Do not delete your database.**

## Windows Command Prompt

Extract this zip and open **CMD** (not PowerShell) inside the extracted directory.

```cmd
cd /d C:\Users\Admin\Downloads\AcademyFlow_Migration_Repair_Source
REPAIR_MIGRATIONS.cmd
```

The batch script checks for `dotnet ef`, installs EF Core tool version 10.0.13 only if missing (internet access to NuGet required), then generates `AcademyModules` using the official EF design-time scaffold and runs `has-pending-model-changes`. You should see `SUCCESS: Model and migrations are in sync.` The command **does not touch the database**. Review the generated migration before applying it to Render/Neon; do not deploy the original broken migration files.

Now start the app using the local database settings in the **same CMD window**:

```cmd
set "ASPNETCORE_ENVIRONMENT=Development"
set "ConnectionStrings__AcademyDb=Host=localhost;Port=5432;Database=academyflow_local;Username=postgres;Password=YOUR_POSTGRES_PASSWORD;SSL Mode=Disable"
set "ADMIN_EMAIL=admin@academy.com"
set "ADMIN_PASSWORD=YOUR_UNIQUE_PASSWORD_12_CHARS_MIN"
dotnet run --project server\Academy.Api\Academy.Api.csproj --urls http://localhost:5180
```

In a second CMD window:

```cmd
cd /d C:\Users\Admin\Downloads\AcademyFlow_Migration_Repair_Source\client
npm ci
npm start
```

If the existing PostgreSQL DB has already applied `20261018120000_AcademyModules` (possible only if the earlier manual migration was run under a suppressed warning), the generated replacement has a different ID and must not blindly be applied. Inspect `SELECT * FROM "__EFMigrationsHistory";` first. Otherwise the original error occurred before applying new migrations and the database can be kept.

After running the repair, the **generated** migration files must be included in any future Git commit / Render deployment. This zip by itself is not a deployable repaired backend until the official migration has been generated. Keep `Dockerfile`, `render.yaml`, EF project version, and Angular build and proxy settings untouched.
