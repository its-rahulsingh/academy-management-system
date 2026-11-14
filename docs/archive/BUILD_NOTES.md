# Validation status — 2026-10-18

## Confirmed offline

- All seven original deployment-critical files tested against the uploaded ZIP remained **byte-for-byte identical**: Dockerfile, render.yaml, client/angular.json, client/proxy.conf.json, API csproj, API appsettings.json, original InitialAcademySchema migration.
- `python scripts/source_checks.py`: passed.
- TypeScript application source: parses / transpiles without TypeScript syntax errors using globally installed TypeScript 5.8.3.
- TypeScript logic: strict `tsc --noEmit` passed against minimal temporary Angular HTTP API type declarations. These are **stubs** and do not validate Angular templates or real Angular 21 library types.
- `client/src/app/app.html`: opening/closing native HTML tags balanced in a Python HTML parser.
- Auth policy inspection: admin-created accounts, no public signup, cookie XSRF, auth guarding private API routes and POST payload validation.
- The original API health and welcome routes and Angular output path remain in source.

## Not verifiable in this environment

- `npm ci` and `npm run build`: dependencies could not be installed because npm registry DNS calls fail with `EAI_AGAIN`; no `ng` CLI is installed. **Angular production compilation and template type checking were NOT run.**
- `dotnet build` / `dotnet test`: the .NET 10 SDK is not installed here and cannot be fetched via this environment. **C# compilation was NOT run.**
- PostgreSQL migrations were not applied, because credentials and access to the user's Neon instance were not provided.
- Docker image build, live Render deployment, authentication, data CRUD integration, and browser screenshot comparisons were NOT run.

## Mandatory pre-deployment acceptance checks

1. **Before deployment:** execute `cd client && npm ci && npm run build` on a Node 22 machine.
2. Run `dotnet restore server/Academy.Api/Academy.Api.csproj` and `dotnet build server/Academy.Api/Academy.Api.csproj -c Release` with .NET SDK 10. Fix any real compiler errors before pushing.
3. Back up Neon. Verify migrations before applying to an existing production database. Do not skip this step.
4. Build the full Docker image from the extracted **combined source root** and test login, student records, fee receipts, payouts, tests, role restrictions and restart safety.
5. Only after these pass, push combined root contents to the Git repository connected to the existing Render service.

This is a **source implementation package requiring validation**, not a claim of production readiness.
