# Agro-Allied Global Trade Platform

A **cross-border import/export platform** for agricultural produce — connecting African
exporters, importers, suppliers, farmers, agro-processors, logistics providers, and
international buyers. Pan-African and global, corridor-neutral: every business sets its
own origins and destinations. A domestic retail marketplace is a follow-on phase.

**Core trade journey:** Discover → Source → Verify → Negotiate → Trade → Ship → Deliver

> **Status: Phase 1 (cross-border sourcing core) complete; Phase 2 (trade execution) through task 2.7.**
> The full journey is *Discover → Source → Verify → Negotiate → Trade → Ship → Deliver*; the
> first four steps are built, and the fifth (order placement, payment, trade
> paperwork, consignment tracking, and the administrative oversight of all of it) is landed
> through task 2.7. See
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

Integration tests (129 tests) spin up the API against a local `AgroTrade_Tests` database
(`TestingWebAppFactory` migrates it automatically). Point `AGROTRADE_TEST_CONNECTION` at
another SQL Server to run tests somewhere else (CI does this).

> **Note:** that database is **not** reset between runs, and test classes run in parallel
> against it. Assertions are therefore written relative to a baseline measured in the same
> test, or scoped by a per-run-unique marker — a fixed token accumulates across runs and
> breaks any exact-count assertion.

> **Note (Windows):** if `dotnet test` fails with `0x800711C7 — An Application Control
> policy has blocked this file`, Windows Smart App Control is flagging the freshly built
> test assembly. Enable **Developer Mode** (Settings → Privacy & security → For developers)
> — the official workaround that disables SAC for local development. CI is unaffected.

## Current status

Phases 0 and 1 are complete. Every numbered task in Phase 2 (2.1–2.7) has landed.

Two admin queues named in the [Phase 2 scope](docs/01-roadmap.md) are **not** built: product
moderation (`PATCH /admin/products/{id}/moderation`) and the admin RFQ list (`GET /admin/rfqs`).
Both remain planned; see [`docs/03-api-surface.md`](docs/03-api-surface.md).

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
| 2.4 | Payment gateway adapter #1 (sandbox) + idempotent webhook | **Done** |
| 2.5 | Off-platform payment recording + transaction ledger | **Done** |
| 2.6 | Shipment record + tracking events + guest tracking | **Done** |
| 2.7 | Admin dashboard (all queues + KPIs) + audit log | **Done** — 5 of the 7 queues the roadmap names; products moderation and the admin RFQ list are not built |

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

Task 2.4 added the payment gateway seam and the first adapter behind it: a **sandbox gateway**
with no third-party SDK and no provider account. `IPaymentGateway` is deliberately tiny, and
gateway selection runs through `IPaymentGatewayResolver`, which matches an order's currency
against each registered adapter's `SupportedCurrencies`. `PaymentProvider` identifies *who* the
counterparty is and is append-only, so a second corridor is a registration rather than a new enum
value per currency. The sandbox settles **USD only**; an order in another currency is refused with
`CORRIDOR_UNSUPPORTED` before any payment row exists, rather than raising a charge that would
never arrive.

The sandbox deliberately does **not** settle inline. It raises a `pending` charge with a
checkout URL, and settlement arrives later as a signed event pushed through the real
`POST /payments/webhooks/{provider}`. A sandbox that flipped `pending → paid` synchronously would
prove nothing about the four things this task exists to get right: asynchronous delivery,
at-least-once redelivery, out-of-order events, and signature verification.

Those defences, each with a test:

- **Signature.** HMAC-SHA256 over the exact bytes received, compared in constant time, with an
  optional `sha256=` prefix. The endpoint takes the raw body rather than a bound model, because
  re-serialising would produce different bytes. A bad signature is 400, not 401 — the caller is
  not a user of ours and cannot log in.
- **Idempotency.** A unique index on `PaymentWebhookEvents (Provider, ExternalEventId)`.
  Deduplication is the *failed insert*, not a pre-check, because a pre-check races with a
  concurrent redelivery. A redelivery is answered **200**; any other status tells the gateway to
  retry an event we already applied.
- **Ordering.** An event older than `Payment.LastEventAtUtc` is recorded but not applied, so a
  late `payment.failed` cannot undo a `payment.succeeded`.
- **Outbound retries.** `Payment.IdempotencyKey`, client-suppliable and uniquely indexed. The
  same key with a different order or amount is a 409 `IDEMPOTENCY_KEY_REUSED`, so the guard
  cannot hide a client bug behind a success.

Settlement writes a `Transactions` row and rolls the order's `PaymentStatus` up, with a filtered
unique index on `Transactions (PaymentId)` so a payment cannot be double-counted. The uploader
of an event is not trusted to be the payer: a gateway settles its own charges, and the sandbox's
"simulate" affordance lives in a separate service so `PaymentService` never names the sandbox.

Two latent defects were found and fixed while testing this against the live API, both invisible to
the suite as first written:

- The payment was never added to the change tracker, so `SaveChanges` succeeded having written
  nothing — payments were returned by the API but never persisted.
- Inbound event timestamps were compared as raw `DateTime` ticks, which ignores
  `DateTime.Kind`. A gateway sending `14:00:00+01:00` read as an hour *later* than a genuine
  `13:00:00Z`, which was enough for a stale `payment.failed` to overwrite a settled payment. The
  ordering decision now normalises to UTC first. Both are covered by tests that fail without the
  fix.

Webhook bodies are parsed case-insensitively and the sandbox emits camelCase, matching what real
gateways send. Under the strict default the sandbox round-tripped its own PascalCase perfectly and
every test passed — but a real provider's camelCase payload would have deserialised to nulls and
been rejected as malformed.

Task 2.5 adds the other rail: the **seller records** a bank transfer or letter of credit that has
already landed, at `POST /orders/{id}/payments/manual`. Recording is the seller's job, not the
buyer's, and that inversion is the design. The buyer is the party that sends the money, but only
the beneficiary of the account can see it arrive — a buyer attesting to their own transfer would
let anyone mark an order paid and demand dispatch against funds the platform cannot verify exist.
It is the deliberate mirror of task 2.4, where the buyer is the only party who may initiate.

A recorded manual payment is `Paid` immediately, with a `valueDateUtc`. There is no provider to
poll, so a `pending` state here would only mean "waiting for a human to come back and finish
typing this"; the record would exist solely to be updated. Partial payments are the norm rather
than an edge case — a T/T deposit against the balance is the common trade term — so several may
be recorded per order, each with its own trace id.

The bank's **remittance reference is required and unique per order**, enforced by a filtered unique
index on `Payments (OrderId, RemittanceReference)`. It is the one externally-checkable detail on a
record nobody witnessed, and it is what makes a duplicate detectable: re-recording the same
transfer is the likeliest mistake here, and without the guard it silently doubles the order's
paid figure. Scoped per order rather than globally, because trace ids are only unique within a
bank and two different banks can legitimately issue the same string. The ledger's
`ExternalReference` carries that trace id — the column an auditor reconciles against — rather than
being left null for manual transfers.

Validation refuses an amount above the outstanding balance, a future value date (which would
describe money that has not arrived), a blank remittance reference, and `PaymentMethod.Gateway` —
hand-typing a gateway charge id would invent an identity no charge will ever match. Currency is
always the order's own; there is no FX anywhere in the system, and a multi-currency settlement
would need two entries in two currencies, not a converted one.

`GET /orders/{id}/transactions` reads the ledger: one immutable row per movement of money, oldest
first, both parties. It is deliberately a separate read from the payments list, because a payment
is a *mutable attempt* and a ledger entry is a *fact* — a gateway settlement and a bank transfer
appear in the same list, which is what makes it reconcilable against a bank statement.

Writing that read endpoint exposed a defect in the settlement logic from task 2.4. A settled
payment could be flipped to `Failed` by a *newer* `payment.failed` event: the ordering defence
compares timestamps, so an event stamped half an hour after the success sails through, because by
timestamp it is the newest word. The visible result was an order whose ledger claimed 86,000
against a 43,000 order — the payment said failed, the ledger kept saying settled, and nothing
recorded that they disagreed. **Settlement is now terminal.** A failing event for an already-paid
payment is recorded with outcome `SupersedesSettlement` and not applied, because money that
settled on a card rail does not un-settle; a gateway that really did claw the money back is
describing a refund, which needs a compensating ledger entry rather than a status flip. Two tests
cover it, both verified to fail without the guard. Refunds remain out of scope until they can be
booked properly.

One caveat worth stating plainly. Recording a manual payment spans **three** commits — the payment
row, then the ledger row, then the order rollup and notifications — because `PAY-` and `TXN-`
references come from two different max-plus-one sequences and each allocator owns its own
transaction. A request that fails between them could leave a settled payment with no ledger entry.
That is repaired rather than prevented: the idempotent replay path rewrites a missing ledger row,
which is free when nothing is broken and is exactly what a retry is for. Making it truly atomic
needs an allocator that can number two sequences inside one transaction, which is a change to
infrastructure shared with RFQ and order numbering and was not this task's to make.

### Task 2.6 - Shipment record + tracking events + guest tracking

The seller books a consignment against an order, then appends milestones as the goods move. Both
parties read the same timeline, and anyone holding the guest tracking link reads a narrowed view
of it.

**A shipment has no `Status` column.** Its status *is* its latest milestone. That is the central
decision in this task, and it was taken because the alternative is a second source of truth that
can drift: a stored status that says `Delivered` while the timeline ends at `Departed` is a
statement the buyer can verify is wrong, and every status reported anywhere in the system would
then rest on which of the two you happened to read. Deriving it makes the status correct by
construction, and it guarantees that anything shown as a status is backed by an actual dated,
located, described event. `DepartedAtUtc` is derived the same way — from the *earliest* `Departed`
milestone, since a re-routed consignment leaves twice and the second sailing must not rewrite when
it first left.

The milestones are **append-only**. There is no update or delete path anywhere in the API or the
model — not a permission flag, just no code. The timeline is buyer-facing evidence of what a
carrier reported and when, and a trade where "we corrected the record" is available is one where the
record proves nothing. Statuses are correspondingly *not* forced to advance: goods that have been
delivered genuinely do generate a customs exception afterwards, and refusing that entry would only
teach the seller to stop recording what happened. Two rules the append path does enforce, both
because status is the latest event — a milestone cannot be dated in the future
(`MILESTONE_IN_FUTURE`), and ingested timestamps are normalised to UTC first. The second is the
same `DateTime.Kind` trap task 2.4 hit on webhook events: a raw `DateTime` comparison ignores
`Kind`, so `14:00+02:00` would read as two hours *later* than a genuine `13:00Z` and take over as
the current status.

Booking writes milestone #0, and its date is caller-suppliable (`initialOccurredAtUtc`, defaulting
to now). This is not a convenience. Because every later milestone is sorted against the booking,
a seller logging up a consignment that departed last week has no way to write a coherent history
without it: the booking would be stamped today, every correctly-dated real milestone would sort
beneath it, and the shipment would report `Booked` while sitting at sea. Correct data producing a
wrong answer is worse than a missing field, so the field is there.

**One consignment per order**, enforced by a unique index on `Shipments.OrderId`. Splitting a
consignment needs per-order-item quantity allocation, which the order model does not express;
`GET /orders/{id}/shipments` returns a list anyway, so lifting this is not a breaking change for
anything holding a parsed response.

**The guest token is stored hashed** (SHA-256 hex, from the `RefreshTokenFactory` generalised to
`OpaqueTokenFactory`, since refresh tokens and tracking tokens have identical crypto requirements).
The raw value is returned exactly once — at creation and at rotation — and no endpoint can recover
it, so a database dump yields no working tracking links. `POST /shipments/{id}/tracking-token`
issues a fresh one and kills the previous link, which is a real kill switch: these links get
forwarded, and a forwarded link cannot be recalled.

The anonymous page is deliberately narrow. `ShipmentPublicTrackingDto` is a separate type, not
`ShipmentDto` with fields hidden, so the frontend cannot leak what it was never sent: route,
carrier, tracking reference, mode, ETA, incoterm, status, milestone timeline. No order number, no
amounts, no currency, no party names, no product, no documents. Someone holding a forwarded link
needs to know where the container is and has no business learning who traded what for how much. It
lives at `/track/:token` on its own `[AllowAnonymous]` controller, because the link goes to freight
forwarders and customs agents who have no account here and will not make one. Unknown or rotated
tokens get 404 rather than 401 or 403, so the page cannot be used to tell a live link from a dead
one.

Two things this task deliberately does not do. It does **not** auto-transition the order from
shipment events — the order state machine stays the single authority, consistent with task 2.2. And
it does not guess `OriginCountry`: `Orders` records `PortOfLoading` and `DestinationCountry` but no
origin country, so there is nothing to inherit, and inferring one from the seller's registered
country would put a plausible-looking wrong country on a page the buyer trusts. That is a gap in the
2.1 order model, now recorded in `docs/02-data-model.md` rather than papered over.

One pre-existing caveat, surfaced while testing and not fixed here because the fix is
cross-cutting. The API's timestamps serialise without a zone, because SQL Server's `datetime2`
carries none and `System.Text.Json` only appends `Z` for a `Kind=Utc` value. Every DTO in the
system — orders, payments, shipments — has always had this shape, and the frontend has been
formatting it in the browser's local zone. Since the wire value is in fact a UTC wall-clock
reading, that shifts displayed times by the reader's UTC offset. The new shipment UI opts out by
formatting with `timeZone: 'UTC'` explicitly, because a page whose entire job is saying *when* a
container moved is where this matters most. Fixing it properly means either a global converter or
auditing every date parse on the client; that is its own task.

### Task 2.7 - Admin dashboard + audit log

Everything an administrator needs to see, on one screen and behind one endpoint:
`GET /admin/overview` returns platform counts, four verification and trade funnels, corridor
value, per-category supply against demand, and RFQ demand, in a single request. It is one
endpoint rather than eight because a dashboard is one screen — splitting it would mean eight
round trips and a loading state per tile, and the tiles would render against each other only
by accident. The queues it links to (`/admin/audit-log`, `/admin/orders`, `/admin/shipments`,
`/admin/users`) are separate endpoints, because each is a paginated list with its own filters.

**The audit log records admin decisions, not entity edits.** `AuditLogWriter.Record` is called
explicitly at the two places an administrator actually decides something — business
verification and document review — and nowhere else. The tempting alternative is a
`SaveChanges` interceptor that logs every changed entity, and it is the wrong shape here: it
would bury the entries that matter (who approved what, and when) among thousands of
`UpdatedAtUtc` stamps, and those stamps are already derivable from the row itself. An audit
trail that cannot be read is not one. `Action` and `EntityType` are **text, not enums**,
precisely so that adding a new admin action does not require a migration to log it.

`Record` is synchronous and deliberately **save-less**: it adds the entry to the change
tracker and lets the caller's own `SaveChangesAsync` flush it. The approval and the record of
the approval therefore commit in one transaction and cannot come apart — which is the only
property that makes the log worth keeping. `AuditLog.UserId` is `ON DELETE RESTRICT`, never
cascade: deleting a user must never delete the evidence that they approved a transaction, and
`SearchAuditLogAsync` substitutes the deleted account's id in place of the display name rather
than blanking the row.

**Corridor figures are grouped by `(PortOfLoading, DestinationCountry, Currency)`.** Currency is
part of the row's identity, not a label on it. There is no FX rate anywhere in this system, so
a single "total trade value" across an NGN corridor and a USD corridor is a sum of incomparable
numbers wearing a decimal point. The panel shows **gross order value and net settled payments
side by side** rather than picking one, because they answer different questions and collapsing
them hides unpaid orders — a corridor can look healthy on gross volume while nothing has been
paid. Net settled is computed from the **transaction ledger**, not the order's payment rollup,
so a ledger entry denominated differently from its order forms its own row and is surfaced as
`mismatchedLedgerEntries` instead of quietly distorting a total.

The funnels count **orders** by payment status, not payments by payment status, and render
every enum stage including the empty ones. An omitted stage is indistinguishable from a bug; a
rendered zero is information.

Supply and demand are **merged per category, driven by the union of both sides**. A category
with RFQs and no listings is exactly the catalogue gap this panel exists to reveal, and
iterating the product list would omit it silently. Each row carries `rfqCount` and
`openRfqCount` separately: the flag for a live gap is based on *open* RFQs, because a category
whose only RFQ closed years ago is a history, not a demand signal. For the same reason
`demand.preferredCurrency` has **three** states — the agreed currency, `"Mixed"` when open RFQs
genuinely disagree, and empty when none are open. Reporting `"Mixed"` for an empty set asserts
that buyers want several currencies at once when there are no buyers at all. `averageBudgetCeiling`
is withheld rather than approximated whenever open RFQs disagree on currency.

Corridor rows are uncapped; the commodity panel stays capped at 12 as an explicit "top N". The
corridor an administrator opens the page to inspect is frequently the low-volume one, and
silently omitting it reads as "no shipments".

Two smaller API decisions. Admin list **status labels live in `AdminService`, not in the DTOs** —
a status enum must not leak into a client contract, and the one exception
(`AdminFunnelStageDto.Label`) is deliberate, because a funnel stage with no name is not
renderable. And the user queue's role filter takes a role **name**, not a role id: ids are
database-generated and undiscoverable through the API, so an id filter is a parameter no client
could fill.

Four defects were found while building this, all invisible to the suite as first written:

- `Task.WhenAll` over one scoped `DbContext` to assemble the overview. EF forbids concurrent
  operations on a single context, so every request 500'd under load. The aggregates are now
  awaited sequentially.
- `ApplicationUser` never stamped `CreatedAtUtc` — 3,194 null rows were found in the test
  database. The entity now implements `IAuditableEntity`, so it picks up stamping it was
  already eligible for but never receiving.
- The commodity panel omitted demand-only categories (fixed above), and the corridor list was
  silently capped at 50.
- The shipment queue's status filter matched nothing, because status is a *method call* on the
  loaded entity rather than a column: `SearchShipmentsAsync` filters in memory, and
  `.Include(s => s.Events)` is mandatory or the filter has nothing to evaluate against.

Admin timestamps are formatted with `timeZone: 'UTC'` and labelled "UTC", extending the
caveat recorded in task 2.6. Every admin page opts into the one reading of a zone-less
timestamp that can be trusted. Admin queue ordering puts null timestamps last —
SQL Server sorts NULLs first on a descending sort, so without `OrderByDescending(u => u.CreatedAtUtc != null)`
unknown-age users would dominate a "newest first" queue.

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