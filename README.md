# Agro-Allied Global Trade Platform

A **cross-border import/export platform** for agricultural produce — connecting African
exporters, importers, suppliers, farmers, agro-processors, logistics providers, and
international buyers. Pan-African and global, corridor-neutral: every business sets its
own origins and destinations. A domestic retail marketplace is a follow-on phase.

**Core trade journey:** Discover → Source → Verify → Negotiate → Trade → Ship → Deliver

> **Status: Phase 0 (foundations) in progress.** Monorepo with a functioning ASP.NET Core
> API (auth & identity complete) and a React web app scaffolded and wired to it.

## Repository Layout

| Path | Contents |
| --- | --- |
| `backend/` | ASP.NET Core Web API solution (Domain / Application / Infrastructure / Api / Tests) |
| `frontend/agro-trade-web/` | React 19 + Vite + TypeScript + Tailwind v4 + React Router + Axios web app |
| `docs/` | Technical plan (roadmap, data model, API surface, architecture, MVP scope, decisions) |
| `.github/workflows/ci.yml` | CI: backend build + tests against a SQL Server container, frontend lint + build |

## Technology Stack

| Layer      | Technology                                                                   |
| ---------- | ---------------------------------------------------------------------------- |
| Frontend   | React 19 + TypeScript + Tailwind CSS v4 + React Router + Axios                |
| Backend    | ASP.NET Core Web API (.NET 10 LTS) + C#                                      |
| ORM        | Entity Framework Core 10                                                      |
| Database   | SQL Server (local instance available: `MSSQLSERVER` running)                  |
| Auth       | ASP.NET Core Identity + JWT (rotation + reuse detection) + role-based auth    |
| Storage    | Local disk in dev → cloud (Cloudinary / AWS S3) via an abstraction            |
| Realtime   | SignalR (notifications, messaging)                                            |
| Background | .NET hosted services / Quartz (expiries, payment timeouts, email queue)       |
| Payments   | Gateway adapter per market (NGN, USD, EUR, GBP, CNY) — provider TBD           |

## Prerequisites

- .NET SDK **10.x**
- SQL Server (Developer/Express) reachable at `localhost`; PowerShell user `dotnet-ef`
  to create migrations if needed
- Node.js **22+** (npm)
- For the test suite: `dotnet test` (see note below about Smart App Control)

## Run the backend (Development)

```powershell
# 1. Configure secrets once (contents shown are placeholders):
dotnet user-secrets init --project backend/src/AgroTrade.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=AgroTrade;Trusted_Connection=True;TrustServerCertificate=True;" --project backend/src/AgroTrade.Api
dotnet user-secrets set "Jwt:Secret" "<32+ random characters>" --project backend/src/AgroTrade.Api
dotnet user-secrets set "Seed:AdminEmail" "admin@agrotrade.local" --project backend/src/AgroTrade.Api
dotnet user-secrets set "Seed:AdminPassword" "<strong password>" --project backend/src/AgroTrade.Api

# 2. Apply schema migrations (creates the AgroTrade database):
dotnet ef database update --project backend/src/AgroTrade.Infrastructure --startup-project backend/src/AgroTrade.Api

# 3. Run (http://localhost:5000):
dotnet run --project backend/src/AgroTrade.Api --launch-profile http
```

On startup in Development the API applies migrations, seeds roles + the administrator,
and writes email confirmation / password-reset tokens to the log (no real email sender yet).

## Run the frontend

```powershell
cd frontend/agro-trade-web
npm install
npm run dev        # http://localhost:5173 — proxies /api to http://localhost:5000
```

## Test

```powershell
dotnet build backend/AgroTrade.slnx -warnaserror
dotnet test backend/AgroTrade.slnx
```

Integration tests (16 tests) spin up the API against a local `AgroTrade_Tests` database
(`TestingWebAppFactory` migrates it automatically). Point `AGROTRADE_TEST_CONNECTION` at
another SQL Server to run tests somewhere else (CI does this).

> **Note (Windows):** if `dotnet test` fails with `0x800711C7 — An Application Control
> policy has blocked this file`, Windows Smart App Control is flagging the freshly built
> test assembly. Enable **Developer Mode** (Settings → Privacy & security → For developers)
> — the official workaround that disables SAC for local development. CI is unaffected.

## Phase 0 — What's working

Backend (verifiable live, and covered by the integration suite):

- Registration with multi-role selection, login, `/auth/me`, profile update at `/me`
- JWT access tokens + refresh-token rotation with **reuse detection** (replay revokes the
  whole token family) and logout
- Email confirmation, forgot/reset password (ConsoleEmailSender stub in dev)
- Roles (`Administrator`, `Buyer`, `Farmer`, `Supplier`, `AgroProcessor`, `Exporter`,
  `Importer`, `Distributor`, `LogisticsProvider`) with role-based authorization policies
- Uniform ProblemDetails error contract (`code`, `detail`, `fieldErrors`), validation via
  [FluentValidation], rate limiting on `/auth`, CORS, Serilog, health endpoints
  (`/health` liveness, `/health/ready` readiness)

Frontend (builds clean; wired to the API):

- Landing page, sign-in, registration with role chips, forgot/reset password, confirm email
- Protected dashboard with profile editing; axios client with bearer-token attachment and
  transparent access-token refresh on 401

## Documentation Index

| Document | Purpose |
| --- | --- |
| [`docs/README.md`](docs/README.md) | Index of planning docs |
| [`docs/01-roadmap.md`](docs/01-roadmap.md) | Phase-by-phase delivery roadmap |
| [`docs/02-data-model.md`](docs/02-data-model.md) | Core entities & relationships |
| [`docs/03-api-surface.md`](docs/03-api-surface.md) | Endpoints, contracts, status codes |
| [`docs/04-architecture.md`](docs/04-architecture.md) | Architecture & cross-cutting concerns |
| [`docs/05-mvp-scope.md`](docs/05-mvp-scope.md) | MVP scope and out-of-scope items |
| [`docs/06-decisions-and-risks.md`](docs/06-decisions-and-risks.md) | Key decisions & open risks |