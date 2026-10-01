# Agro-Allied Global Trade Platform

A **cross-border import/export platform** for agricultural produce — connecting African
exporters, importers, suppliers, farmers, agro-processors, logistics providers, and
international buyers. Pan-African and global, corridor-neutral: every business sets its
own origins and destinations. A domestic retail marketplace is a follow-on phase.

**Core trade journey:** Discover → Source → Verify → Negotiate → Trade → Ship → Deliver

> **Status: Phase 1 (cross-border sourcing core) complete; Phase 2 (trade execution) through task 2.3.**
> The full journey is *Discover → Source → Verify → Negotiate → Trade → Ship → Deliver*; the
> first four steps are built, and the fifth (order placement, fulfilment, and trade
> paperwork) is landed through task 2.3. See
> [Current status](#current-status) below and [`docs/01-roadmap.md`](docs/01-roadmap.md)
> for the full phase plan.

> **Product pivot:** the demo data runs as a single supplier — Achievers Global Consult —
> rather than a multi-supplier marketplace. The domain model still supports multiple
> businesses per RFQ; only the seeded demo has one seller.

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

Plain `dotnet build` and `dotnet test` are fine, from any directory.

A running `AgroTrade.Api` memory-maps the DLLs in its own `bin` folder, so
MSBuild cannot overwrite them and would otherwise fail with `MSB3021`/`MSB3027`
("the file is locked by AgroTrade.Api"). Those are file-copy errors, not compile
errors. `backend/Directory.Build.targets` stops the API before each build so this
never surfaces:

```
Stopped running AgroTrade.Api: 10044 [...\src\AgroTrade.Api\bin\Debug\net10.0\AgroTrade.Api.exe]
Build succeeded.
```

Expect that line — the build deliberately stops the API. It only touches processes
started from this repo's own output, and `dotnet run` will happily rebuild and
restart it. Opt out with `dotnet build -p:StopRunningApi=false`. The frontend has
no such restriction; `npm run build` works while Vite is running.

Integration tests (16 tests) spin up the API against a local `AgroTrade_Tests` database
(`TestingWebAppFactory` migrates it automatically). Point `AGROTRADE_TEST_CONNECTION` at
another SQL Server to run tests somewhere else (CI does this).

> **Note (Windows):** if `dotnet test` fails with `0x800711C7 — An Application Control
> policy has blocked this file`, Windows Smart App Control is flagging the freshly built
> test assembly. Enable **Developer Mode** (Settings → Privacy & security → For developers)
> — the official workaround that disables SAC for local development. CI is unaffected.

## Current status

Phases 0 and 1 are complete. Phase 2 is underway (tasks 2.1–2.3 of 2.7 landed).

### Phase 0 — Foundations (complete)

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

### Phase 1 — Cross-border sourcing core (complete)

- Businesses with legal/trading name, country and type; **imports-from / exports-to**
  reachability; document uploads; submit-for-verification with admin approve/reject and a
  **Verified badge** that drives marketplace trust filters
- Export listings carrying real trade terms: origin country, HS code, price + currency,
  available quantity, MOQ, packaging, certifications, incoterms, lead time, port of loading
- Marketplace search/filter by commodity, origin country, exports-to destination,
  verified-only, currency, price range, HS code, with pagination and sort
- RFQs: destination country + port, preferred incoterm, transport mode, required
  certifications, deadline, budget; open directory; supplier quotations carrying complete
  trade terms; side-by-side comparison; negotiation thread
- Messaging (general + per RFQ) and in-app notifications
- Frontend: landing, marketplace, product detail, RFQ publish/browse/compare, negotiation,
  messaging, notifications, business profile + verification, admin queues, my listings/RFQs

Seeded demo: 13 listings across cocoa, cashew, palm kernel, frozen fish, maize and rice,
all under Achievers Global Consult, idempotently re-applied on every startup.

### Phase 2 — Trade execution (in progress)

Tasks 2.1–2.7 per the roadmap:

| # | Task | Status |
| --- | --- | --- |
| 2.1 | Order model (incoterms, ports, payment terms, currency) + handoff from accepted quotation | **Done** |
| 2.2 | Order lifecycle + inventory reservation + buyer/seller inboxes | **Done** |
| 2.3 | Trade document upload + statuses + admin verification queue | **Done** |
| 2.4 | Payment gateway adapter #1 (sandbox) + idempotent webhook | Not started |
| 2.5 | Off-platform payment recording + transaction ledger | Not started |
| 2.6 | Shipment record + tracking events + guest tracking | Not started |
| 2.7 | Admin dashboard (all queues + KPIs) | Not started |

Task 2.1 delivered `Orders` / `OrderItems` / `Payments` / `Transactions` / `TradeDocuments`
plus `POST /orders/from-quotation`, `POST /orders`, and the buyer/seller order reads.
Trade terms are **snapshotted** onto the order at handoff, so later edits to the RFQ or
quotation cannot rewrite what was agreed, and a quotation can only ever be converted once
(enforced by a filtered unique index, not just an application check).

Task 2.2 added the legal state machine (`OrderLifecycle`), `POST /orders/{id}/transitions`,
and `Inventory`. `Product.AvailableQuantity` is a denormalised mirror of
`Inventory.Available = OnHand − Reserved`, written only by `InventoryService`: stock is
reserved when an order is `Placed`, committed on `Confirmed`, and released on `Cancelled`.
Both parties get an order inbox — buyers at `/orders`, the selling business at
`/orders/selling` — and the server, not the UI, decides which transitions a given party may
make.

Task 2.3 filled in `TradeDocuments` as **order-scoped** paperwork. Both parties upload and
both see everything on the order they share, so there is no per-document visibility flag and
a third party gets 404 rather than 403. An upload lands directly in `Uploaded` (a `Draft`
nobody can review is not a state worth having); the admin queue at `/admin/documents` then
verifies or rejects. Rejecting requires notes, re-reviewing to the same verdict is a 409, a
verified document cannot be withdrawn, and cancelled or completed orders are closed to
uploads. The uploader is notified on both verdicts.

Task 2.3 also fixed a latent bug in order and RFQ numbering. Numbers were allocated as
`MAX(suffix) + 1`, a read-then-write that two simultaneous creators can race, losing to a
unique-index violation surfaced as an unhandled 500. `SequenceAllocator` takes a range lock
over the indexed number column (`UPDLOCK, HOLDLOCK` in a serialisable transaction), so a
second allocator blocks and then sees the row it would otherwise have missed.

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