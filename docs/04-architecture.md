# 04 — Architecture

## 1. Repository layout

```
Agritech/
├── docs/                                  # planning docs (this set)
├── backend/
│   ├── AgroTrade.sln
│   ├── src/
│   │   ├── AgroTrade.Domain/              # entities, enums, invariants — zero references
│   │   ├── AgroTrade.Application/         # services, DTOs, interfaces, FluentValidation
│   │   ├── AgroTrade.Infrastructure/      # DbContext, Identity, migrations, IFileStorage, email, SignalR
│   │   └── AgroTrade.Api/                 # controllers, middleware, Program.cs (composition root)
│   └── tests/
│       └── AgroTrade.Tests/               # xUnit: unit + integration (WebApplicationFactory)
├── frontend/
│   └── agro-trade-web/
│       ├── src/
│       │   ├── app/                       # router (data router), providers, route guards
│       │   ├── features/                  # auth, businesses/kyc, catalog (export listings),
│       │   │                              # marketplace, rfq, messaging, orders,
│       │   │                              # trade-documents, payments, shipments, admin,
│       │   │                              # market-intelligence
│       │   ├── components/ui/             # shared UI primitives (button, input, badge, modal…)
│       │   ├── lib/                       # api client, auth store, hooks, format utils
│       │   └── types/                     # TS types mirroring API DTOs
│       └── (vite.config.ts, tailwind config, etc.)
└── .github/workflows/ci.yml               # build + test (once git init'd)
```

## 2. Backend layering

```
Api → Application → Domain
 │        │            ▲
 │        └→ (uses Infrastructure via DI registration)
 └→ Infrastructure → Domain
```

**Rules**
- **Domain**: entities, enums, domain rules (e.g. `Order.CanTransitionTo(...)`,
  inventory can't go negative). No EF packages, no HTTP.
- **Application**: services orchestrate workflows (e.g. `SubmitQuotationService`,
  `OrderPlacementService`), DTOs, `FluentValidation` validators, service interfaces
  (`IFileStorage`, `ICache`, `IPaymentGateway`, `IEmailSender`, `ITimeProvider`).
- **Infrastructure**: `AgroTradeDbContext` (EF Core 10), Identity middleware,
  migrations, storage adapter (local disk ↔ Cloudinary/S3), cache-aside store
  (`IDistributedCache`: in-process memory ↔ Redis), SMTP/MailKit, SignalR hubs,
  background jobs (framework hosted services), audit-log interceptor (SaveChanges).
- **Api**: thin controllers → Application services. `Program.cs` owns DI, middleware
  order (CORS → rate-limit → auth → authorization → endpoints), Swagger config.

## 3. Key NuGet packages

| Purpose | Package |
| --- | --- |
| ORM + SQL | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` |
| Identity | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` |
| JWT | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Validation | `FluentValidation.AspNetCore` |
| Docs | `Swashbuckle.AspNetCore` (+ `.Annotations`) |
| Logging | `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Sinks.File` |
| Storage | Cloudinary `CloudinaryDotNet` or AWS `AWSSDK.S3` (behind `IFileStorage`) |
| Cache | `Microsoft.Extensions.Caching.StackExchangeRedis` (behind `ICache`; in-process memory when no Redis is configured) |
| Email | `MailKit` |
| Jobs | Hosted services (`BackgroundService`) from the framework — no Quartz dependency (see §6) |
| Tests | `xunit`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql` (optional heavy) |

## 4. Authentication & authorization design

- **Identity** manages users/roles; JWT access token (30 min) + opaque refresh token
  (30 days, stored hashed in `UserTokens`).
- **Policies**: `Administrator`, `VerifiedSeller`, `SellerOrAdmin`, `BuyerOrAdmin`.
- **Ownership rules** enforced in services, e.g. `QuotationRepository` methods take
  the caller's user/business ids and return nothing for non-participants.
- Password policies, lockout, and account suspension (`IsActive=false`) checked by a
  per-request middleware after JWT validation.
- Admin endpoints separated under `/admin` prefix (Swagger tags) and audited.

## 5. Frontend architecture

- **Vite 7** + **React 19** + **TypeScript** + **Tailwind CSS v4**; `React Router v7`
  data router with `loader`/`action` patterns and route guards per role.
- **TanStack Query** for server state (cache, pagination prefetch, optimistic order
  status moves); **Axios** instance with interceptors: attach token, on `401` try one
  refresh then redirect to login.
- Feature folders (above); each feature owns its API hooks + pages. Shared UI kit
  (button, input, select, badge, modal, table, toast) built on Tailwind + Radix primitives.
- Public vs app shell layouts: public (home, marketplace, product, business, open-RFQs),
  app shell (dashboard per role), admin shell.
- Env: `VITE_API_URL` (default `http://localhost:5001`), proxy `/api` in dev.

## 6. Cross-cutting services

| Concern | Mechanism |
| --- | --- |
| Errors | Middleware maps exceptions → ProblemDetails with machine `code`; validation errors listed |
| Logging | Serilog request logging with correlation id header; **no PII in logs** (mask emails/phones) |
| Audit | `AuditLog` table via `SaveChanges` interceptor for admin/financial/verification actions (spec §18) |
| Idempotency | `Idempotency-Key` → dedupe table for POST `/orders` and `/payments` (Phase 3) |
| Rate limiting | `AspNetCore RateLimiter` on `/auth/*` and `/messages` (per-token sliding window) |
| CORS | Allowlisted origins from config |
| Storage | `IFileStorage.UploadAsync/GetSignedUrlAsync/DeleteAsync`; local disk in dev, Cloudinary/S3 in cloud |
| Config | `appsettings*.json` + **user-secrets** in dev; Key Vault/environment in cloud |
| Background | `ScheduledBackgroundService` hosted workers: a scoped, idempotent pass on a configurable interval, one bad pass logged and retried rather than killing the worker. Today: RFQ expiry + refresh-token cleanup; the base is where the later payment-timeout, email-queue and ingest jobs belong. `BackgroundJobs:Enabled` is the master switch |
| Health | `/health` (liveness) + `/health/ready` (DB reachability) |
| Trade terms & money | Shared services/labels for incoterms, HS-code starter map, currency formatting per ISO-4217 — used by listings, RFQs, quotes, orders |

## 7. Development setup (this machine)

```bash
# git
git init

# Backend (project names below are conventions; exact csproj names free)
dotnet new webapi -o backend/src/AgroTrade.Api
dotnet new classlib -o backend/src/AgroTrade.Domain
dotnet new classlib -o backend/src/AgroTrade.Application
dotnet new classlib -o backend/src/AgroTrade.Infrastructure
dotnet new xunit -o backend/tests/AgroTrade.Tests
dotnet new sln -n AgroTrade && dotnet sln add backend/src/**/*.csproj backend/tests/*.csproj

# Connection string (local SQL Server instance confirmed running)
# Server=localhost;Database=AgroTrade;Trusted_Connection=True;TrustServerCertificate=True;
dotnet user-secrets init --project backend/src/AgroTrade.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=AgroTrade;Trusted_Connection=True;TrustServerCertificate=True;" --project backend/src/AgroTrade.Api

# migrations
dotnet ef migrations add InitialCreate \
  --project backend/src/AgroTrade.Infrastructure \
  --startup-project backend/src/AgroTrade.Api --context AgroTradeDbContext
dotnet ef database update \
  --project backend/src/AgroTrade.Infrastructure \
  --startup-project backend/src/AgroTrade.Api --context AgroTradeDbContext

# run API (assert http://localhost:5001/swagger)
dotnet run --project backend/src/AgroTrade.Api

# Frontend
npm create vite@latest frontend/agro-trade-web -- --template react-ts
cd frontend/agro-trade-web
npm install
npm run dev           # http://localhost:5173
```

**Dev ports:** API `5001` (https) / Vite `5173` with `/api` proxy. `launchSettings.json`
sets `dotnet watch` as default.

## 8. Testing strategy

- **Unit**: domain rules (order/quote state machines, inventory math), service logic
  with fake repositories and `ITimeProvider`.
- **Integration**: `WebApplicationFactory` + the local SQL Server instance (seeded
  per-test, transaction rollback or DB reset between tests); covers key endpoints incl.
  authz-negative cases (401/403), idempotency replay, RFQ→order handoff.
- **E2E (Phase 3+)**: Playwright for buyer→order→shipment journeys.
- **CI**: `dotnet build` has no warnings → `dotnet test` → schema-drift check.

## 9. Deployment (cloud — decision required, see decisions doc)

- Containerize API (`Dockerfile`, multi-stage) + frontend static build → CDN.
- SQL Server in cloud: Azure SQL (managed) recommended; containerized SQL Server as
  fallback. `ConnectionStrings`/JWT secrets from vault/environment.
- CI/CD: GitHub Actions → build, test, migrate (release pipeline), deploy on main.
- Phase 5 additions: Redis (cache), S3/Cloudinary, Azure App Service or AKS.