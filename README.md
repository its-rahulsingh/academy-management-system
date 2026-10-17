# Academy ERP Starter — ASP.NET Core 10 + Angular 21 + Render

A deliberately small full-stack starter for the Academy ERP. One ASP.NET Core 10 web app serves both its own `/api/*` endpoints and Angular's compiled static files in production, so **only one Render Web Service** is required.

## Project contents

```text
academy-starter/
├── server/Academy.Api/
│   ├── Academy.Api.csproj   # .NET 10 / ASP.NET Core
│   ├── Program.cs           # /api/health, /api/welcome, SPA fallback
│   └── appsettings.json
├── client/                 # Angular 21, standalone component
│   ├── package.json
│   ├── angular.json
│   ├── proxy.conf.json      # Forward /api to .NET during local development
│   └── src/
├── Dockerfile              # Multi-stage: Angular build -> .NET build -> .NET runtime
├── render.yaml             # Optional Render Blueprint for one free web service
├── .dockerignore
└── README.md
```

**Why Angular 21?** This starter uses the supported Angular 21 line, compatible with Node 22.12+ (including the current Node 22 LTS releases). .NET 10 is LTS.

## Prerequisites

For running both projects locally without Docker:
- .NET 10 SDK
- Node.js 22 LTS (Node >=22.12, Node <23) and npm
- Git

For the single-container test: Docker Desktop with Linux containers.

## Local development (two terminals)

### Terminal 1 — .NET API

From the repository root:

```bash
dotnet run --project server/Academy.Api/Academy.Api.csproj --urls http://localhost:5180
```

Verify: http://localhost:5180/api/health

### Terminal 2 — Angular

```bash
cd client
npm install
npm start
```

After the first `npm install`, commit the generated `client/package-lock.json` for reproducible Render builds.

Open **http://localhost:4200**. Angular calls `/api/health` and `/api/welcome`; the local `proxy.conf.json` forwards `/api` calls to .NET on port 5180. Do **not** change the Angular calls to hardcoded Render URLs.

## Docker test — same arrangement used by Render

From the repository root:

```bash
docker build -t academy-starter .
docker run --rm -p 10000:10000 --name academy-starter academy-starter
```

Browse **http://localhost:10000** and **http://localhost:10000/api/health**. You can run `python scripts/smoke_test.py http://localhost:10000` to verify all three starter routes.

Docker first builds Angular with Node, then publishes the .NET app with the .NET 10 SDK, then puts the Angular output into the ASP.NET Core runtime image's `/app/wwwroot` folder. Node and the SDK are **not** included in the final runtime container.

## Deploy to Render (one service)

### Step A — Push the extracted project to GitHub

Create a new **empty** GitHub repository, e.g., `academy-starter`, then run from the extracted project root:

```bash
git init
git add .
git commit -m "Initial Academy .NET 10 and Angular starter"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/academy-starter.git
git push -u origin main
```

Replace `YOUR_USERNAME`. Sign in to GitHub as prompted. The **Dockerfile and render.yaml must be at the repository root**, not inside a parent folder created while uploading.

### Step B — Create the Render service

1. Open https://dashboard.render.com and sign up / sign in.
2. Select **New → Web Service**.
3. Connect your GitHub account and choose `academy-starter`.
4. Use these settings:

   | Render field | Value |
   | --- | --- |
   | Name | `academy-starter` (or another unique name) |
   | Language / Runtime | **Docker** |
   | Branch | `main` |
   | Root Directory | repository root (leave blank) |
   | Dockerfile Path | `./Dockerfile` |
   | Instance Type / Plan | **Free** |
   | Health Check Path | `/api/health` |

5. Click **Deploy Web Service / Create Web Service**. No custom build/start command is needed for Docker.
6. Render builds the image and gives you an HTTPS URL such as `https://academy-starter-xxxx.onrender.com` (actual address assigned by Render).
7. Verify `https://YOUR-ACTUAL-RENDER-URL/api/health`, then open the root URL and press **Test backend connection**. Optionally run `python scripts/smoke_test.py https://YOUR-ACTUAL-RENDER-URL` to verify Angular and both API endpoints.

**Alternative:** Choose **New → Blueprint**, select the repo, and use `render.yaml`. The blueprint creates the same one-service, free-plan setup. You do not need to use both deployment methods.

### PORT / networking

Render expects the app to listen on `0.0.0.0` and the `$PORT` it supplies, commonly 10000. `Program.cs` reads `PORT` and binds appropriately. Docker defaults to port 10000 when `PORT` is not supplied.

### Updates

After editing:

```bash
git add .
git commit -m "Update academy starter"
git push
```

Render automatically redeploys changes for a repo-connected service when automatic deploys are enabled.

## Endpoints

| Endpoint | What it does |
| --- | --- |
| `GET /` | Angular UI |
| `GET /api/health` | `{"status":"healthy",...}` for Render and UI checks |
| `GET /api/welcome` | Demo JSON response consumed by Angular |
| `GET /random-angular-route` | Angular SPA fallback to `index.html` |
| `GET /api/not-found` | JSON 404; it does NOT return Angular HTML |

## Scope and next step

This initial starter has **no database**, user accounts, real ERP records, authentication, or authorization. This is intentional: first validate that Angular ↔ .NET ↔ Render work end to end. Before onboarding real users, add:

1. Neon PostgreSQL with Npgsql/EF Core, migration strategy, and backed-up data.
2. Authentication and server-enforced role-based authorization; never expose personal student records without it.
3. Validation, unique constraints, audit fields, exception handling, and error monitoring.

**Free-tier limitation:** Render free Web Services spin down after inactivity and may have noticeable cold starts. Render cautions that the free tier is not for production workloads. This starter is for initial deployment verification, not a finished ERP.

## Troubleshooting

- **Angular reports API unavailable locally:** Ensure the .NET process is listening on `http://localhost:5180`. Check `client/proxy.conf.json` and reload Angular.
- **Render says "No open ports detected":** Ensure you're using the Docker Web Service and haven't overwritten the Docker entrypoint. `Program.cs` uses the Render `PORT` value when supplied.
- **The root URL returns 404 after Docker deployment:** Check that `npm run build` finished and Angular compiled to `dist/academy-web/browser`. Docker copies that into `wwwroot`.
- **Angular routing gives 404 on page refresh:** `Program.cs` includes the fallback for client-side URLs. Keep API routes prefixed `/api`.
- **Render first request is slow:** Free instances sleep after 15 minutes with no inbound traffic; a wake-up delay is normal.
- **`dotnet` is not recognized:** Install the .NET 10 SDK, not only the runtime.
- **Angular Node version error:** Use supported Node 22 LTS (or another version in Angular's official compatibility table).

Docs: https://render.com/docs/docker · https://render.com/docs/web-services · https://angular.dev/reference/versions · https://learn.microsoft.com/aspnet/core/host-and-deploy/docker/building-net-docker-images
