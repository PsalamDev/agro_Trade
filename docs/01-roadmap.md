# 01 — Delivery Roadmap

Phased delivery plan for the **import & export platform** as the core product.
Each phase is independently shippable and ends with a demonstrable milestone.

**Product position (post-pivot):** a cross-border agricultural trade platform connecting
African exporters, importers, suppliers, farmers, agro-processors, logistics providers,
and international buyers — with no single-country bias. Businesses set their own
origin(s) and destination(s); the domestic retail marketplace is a follow-on phase.

**Mapping to the spec:**

| This plan        | Spec section        | Core value delivered            |
| ---------------- | ------------------- | ------------------------------- |
| Phase 0          | —                   | Foundations (not user-facing)   |
| Phase 1          | §5.3, §5.4 + §13    | Cross-border sourcing core      |
| Phase 2          | §5.1, §5.5, §5.6, §5.7 + §13 | Trade execution (docs, payment, shipment) |
| Phase 3          | §14                 | Trust, multi-currency, intelligence |
| Phase 4          | §14 (domestic add-on)| Domestic marketplace + logistics network |
| Phase 5          | §15 + §19           | Global trade infrastructure     |

**MVP = Phases 0–2** = spec section 13 delivered **around import/export** (verified
parties, cross-border listings, international RFQs, trade documents, payments basics,
shipment tracking). Domestic retail commerce is explicitly deferred (Phase 4).

---

## Phase 0 — Foundations

**Goal:** A runnable end-to-end skeleton: API + frontend + database connected, with
authentication working and corridor-neutral (country-aware) foundations.

**Scope**
- Scaffold backend solution (`Api` / `Domain` / `Application` / `Infrastructure` / `Tests`).
- EF Core 10 + SQL Server, `AgroTrade` database, first migration with Identity schema.
- JWT auth: register, login, refresh token rotation, role-based authorization policies
  (roles include `Exporter`, `Importer`, `Supplier`, `Farmer`, `LogisticsProvider`).
- Frontend scaffold: Vite + React 19 + TypeScript + Tailwind v4 + React Router.
- Login / register / onboarding screens wired to the API.
- Global error handling, Serilog, `/health`, CORS, user-secrets.
- `git init`, `.gitignore`, CI placeholder (build + test).

**Deliverables**
- `backend/AgroTrade.sln` compiling; migrations apply cleanly.
- Register → login → JWT → `/api/v1/auth/me` returns role.
- Country + currency parameters respected in seed config (no fixed single market).

**Exit criteria**
- `dotnet test` green; 401/403 enforced; a fresh DB seeds roles.

---

## Phase 1 — Cross-border sourcing core (MVP)

**Goal:** An importer can discover verified exporters' produce and publish a sourcing
request; exporters respond with quoted trade terms. **Discover → Source → Verify → Negotiate.**

**Scope**
- Profiles, addresses (country-aware), role assignment incl. `Exporter` / `Importer`.
- **Businesses & verification**: registration (legal + trading name, country, type),
  document uploads (registration, tax/ID), submit-for-verification, admin approve/reject
  **Verified badge**; businesses declare **imports-from / exports-to countries**
  (`BusinessTradeCountries`).
- **Export product listings**: origin country, commodity, HS code (customs
  classification), price + currency, available quantity, MOQ, packaging options,
  certifications, **incoterms offered, lead time, port of loading**, draft/active lifecycle.
- **Marketplace**: search/filter by commodity/category, **origin country**, **exports-to
  destination** (via seller trade countries), verified-only, currency, price range,
  HS code, pagination/sort.
- **RFQs (importer sourcing)**: product/description, quantity + unit, **destination
  country + port/city, preferred incoterm, transport mode, required certifications,
  delivery deadline, preferred currency, target specs**; publish → open directory →
  exporter quotations with trade terms (incoterm, ports, lead time, price/currency,
  validity) → buyer comparison → negotiation thread.
- Messaging (general + per RFQ) and in-app notifications.

**Deliverables**
- Full flow: Nigerian importer publishes RFQ (5 MT cashew, rain-fed, FOB Mombasa) → two
  verified exporters quote with trade terms → importer compares, negotiates in-thread.
- Verified badge drives marketplace trust filters end-to-end.

**Exit criteria**
- Search respects origin + destination reachability and verified-only.
- Quotation carries complete trade terms; comparison view renders them side-by-side.

---

## Phase 2 — Trade execution (MVP)

**Goal:** A negotiated trade becomes an order with documentation, payment, and shipment
tracking. **Trade → Ship → Deliver** (with documents and payment).

**Scope**
- **Orders**: created from accepted quotation (lossless) or manual entry; captures
  incoterms, port of loading/discharge, payment terms (`T/T`, `L/C`…), currency,
  destination; order lifecycle + inventory reservation for tracked listings; buyer+
  seller order lists.
- **Trade documents** (core): upload + status workflow for commercial invoice, packing
  list, certificate of origin, bill of lading, phytosanitary certificate, inspection
  certificate, export/import permits; admin review queue (uploaded → verified/rejected).
- **Basic payments**: first gateway adapter (sandbox, one corridor) **and** an
  "off-platform payment recorded" flow for corridors without gateways (bank transfer
  references) → transaction ledger. Idempotency for webhooks.
- **Shipments**: shipment record linked to order (mode air/sea/road/rail, ports,
  carrier), tracking events timeline, guest tracking via token.
- **Admin dashboard**: users, businesses (verification), products, RFQs, orders,
  documents (verification queue), shipments.
- Notifications across all lifecycle events.

**Exit criteria**
- Accept quote → order retains all trade terms; docs uploaded and admin-verified;
  payment recorded (gateway or manual); shipment has ≥1 tracking event viewable by buyer.

---

## Phase 3 — Trust, multi-currency & intelligence (spec §14)

**Goal:** Cross-border trust scales and data drives decisions.

**Scope**
- **Multi-currency gateway matrix**: adapters per corridor (NGN, USD, EUR, GBP, CNY as
  gateways become available); refunds.
- **Reviews & ratings** post-completion; **disputes** workflow.
- **Market intelligence v1**: commodity price series, destination-requirement notes
  (certs/permits per country), trade-trend indicators, market report pages.
- Admin analytics: sales by corridor, RFQ volume, verification funnel, top commodities.
- KYC/KYB scaling options (3rd-party verification provider) — decision D5.

**Exit criteria**
- A corridor can execute a full payment via gateway; another via recorded transfer.
- Intelligence pages render real (seeded) commodity series with trend charts.

---

## Phase 4 — Domestic marketplace & logistics network (add-on)

**Goal:** Extend the network to domestic retail/bulk trade and deeper logistics.

**Scope**
- Domestic buy-now / cart flows for local buyers (`Buyer`/`Customer`), local units,
  domestic delivery rows atop the same orders model.
- Logistics-provider directory + booking; warehousing; insurance hooks.
- Domestic onboarding paths separate from exporter/importer flows.

**Exit criteria**
- A domestic buyer can purchase produce with local delivery; an LSP can be found and
  linked to orders.

**Slice 1 — domestic buy-now + cart (shipped).** Buyers shop server-side carts that hold only
product ids and quantities; every read resolves live listing terms, and checkout re-validates
stock and fans the cart into one order per seller on the existing orders model (DAP/Road,
`Placed`, `DeliveryFee`, `DestinationCountry` = seller origin) with the whole cart's stock
reserved atomically. Listings opt in with `SellsDomestically` + `DomesticDeliveryFee` (seeded
on the four Nigerian catalogue items). Buyer-side access is `Policies.Buyer`
(Buyer/Importer/Distributor). Frontend: a header cart badge, a `/cart` page (grouped by
seller, quantity steppers, delivery-address form, per-order success links) and a buy-now card
on the product page. Domestic onboarding paths and deeper logistics (warehousing, insurance
hooks) remain for later slices.

**Slice 1 hardening (shipped).** The seller listing form now exposes `SellsDomestically` and the
flat domestic delivery fee (and *My listings* badges domestic items), so a real seller — not only
the seeder — can opt a listing into buy-now. At checkout the delivery address is **snapshotted onto
the order** (`DeliveryLine1`…`DeliveryPostalCode`, country in `DestinationCountry`) rather than
written as a hidden address-book row, and it is surfaced on the order page; order cards also show
`Domestic · {country}` and the delivery fee, so both parties can see where the goods go.

**Slice 3 — logistics-provider directory + booking (shipped).** A `LogisticsProvider` is a profile
owned by a user holding the (already-seeded) `LogisticsProvider` role; it is deliberately **not** a
`BusinessType`, because a provider is discovered and booked, not verified and transacted-with.
Coverage is a child table of ISO-3166 alpha-2 countries; transport modes and ancillary services are
bitmasks (`ShipmentMode` bits via `ShipmentModeMask`; `[Flags] LogisticsService`). The directory
list is **public** and carries no contact details — it is a front door for people deciding whether
to register — while the full profile, including contact details, requires an account. Either party
to an order may raise a `LogisticsBooking` against it; the named provider accepts or declines, the
requester withdraws an un-answered request, and the requester closes an accepted one out. A booking
references its provider by FK and never a `Shipment`, so it leaves the seller-only, one-per-order
consignment model untouched. Three verified providers are seeded in development. This closes Phase
4's exit criterion that an LSP can be found and linked to orders.

**Slice 3b — LSP ratings (shipped).** The provider's `Rating`/`RatingCount` were seeded-only, which
made the directory's ranking a fixed decoration. A `LogisticsProviderRating` gives them a write
path: once a booking is `Completed`, its requester leaves one rating (1–5, optional comment) and the
provider's headline moves with it (`(Rating × RatingCount + new) / (RatingCount + 1)`), so the
seeded reputation blends with real scores rather than being wiped by the first one. Ratings are
kept separate from `Reviews` — a review is about a business through an order, a rating about a
provider through the leg it carried — and are read on the provider page and carried back on a
booking as `myRating`, so the card can confirm "you rated this" without a second fetch.

**Slice 4 — booking services, insurance hook & destination requirements (shipped).** A booking now
carries the ancillary services the requester is engaging as a `[Flags] LogisticsService` bitmask, and
it must be a subset of what the provider actually offers — warehousing from a provider that does not
warehouse is a `SERVICE_NOT_OFFERED` 400 rather than a silent trim. The `Insurance` bit carries its
own hook, `InsuredValue` (`decimal(18,2)`): engaged insurance requires a declared value and a declared
value requires insurance, so neither half can stand alone. Separately, the documented
**destination-requirements helper** is now built — a pure function over an origin/destination pair
that returns the trade documents a corridor is likely to need (mandatory vs suggested, each with a
one-line reason) plus a corridor note, served anonymously at `GET /marketdata/destination-requirements`
and surfaced as a lookup card on the market-intelligence page.

**Slice 5 — domestic onboarding path (shipped).** Signup now asks how a user will trade and routes
them down one of two paths. A **domestic** registrant is granted `Buyer` and lands on the marketplace
filtered to buy-now listings via a new `domesticOnly` filter on `GET /products`, so a local buyer
registers and shops without the exporter/importer business-verification apparatus. An **international**
registrant picks `Importer`/`Distributor` and lands on the dashboard, where the business profile and
verification prompts live. The dashboard carries a **Buy locally** entry point and the cart for buyer
roles, and its framing adapts for a domestic-only account. Along the way the client's ad-hoc
"registrable == buyer" proxy was replaced by a single `BUYER_ROLES` mirror of `RoleNames.BuyerRoles`,
so the cart badge, buy-now card and permission checks all read the real buyer roles. This completes
Phase 4.

---

## Phase 5 — Global trade infrastructure (spec §15, §19)

**Goal:** Operationally hardened global-trade network.

**Scope**
- Escrow-style transactions via **regulated financial partner** (legal dependency).
- OS/spedition integrations: customs declarations, port systems.
- AI-assisted supplier/buyer matching and trade-document processing (pilot).
- Redis caching, cloud storage migration, background workers at scale, audit exports.
  - **Audit exports delivered** — an admin CSV download of the audit trail and the transaction
    ledger, reusing each list's filters with no page cap (`/admin/audit-log/export`,
    `/admin/transactions/export`).
- Mobile via the same versioned API.

**Exit criteria**
- Load-tested at target concurrency; security review passed; escrow + AI governed by
  signed partnerships (decisions, not code, until partners exist).

---

## Dependency graph

```
Phase 0 (foundations/auth)
   └─ Phase 1 (business verify + exporter listings + RFQ/quotations)
          └─ Phase 2 (orders + trade docs + payments + shipments)
                 ├─ Phase 3 (multi-currency, reviews/disputes, intelligence)
                 ├─ Phase 4 (domestic commerce + logistics network)
                 └─ Phase 5 (escrow, AI, customs, scale)
```

Hard dependencies: verification before Verified-badge filtering · messaging before
negotiation · quotations before order handoff · orders before documents/payments/
shipments.

---

## Workstream task breakdown (Phases 0–2)

S < 1 dev-day · M 1–2 dev-days · L 3–5 dev-days.

### Phase 0
| # | Task | WS | Size |
| - | ---- | -- | ---- |
| 0.1 | Backend solution + DB context + SQL Server config | Backend | S |
| 0.2 | Identity: user/roles (incl. Exporter/Importer), migration | Backend | M |
| 0.3 | JWT issuance/refresh/rotation + role policies | Backend | M |
| 0.4 | Error middleware + ProblemDetails + Serilog | Backend | S |
| 0.5 | Frontend scaffold + Tailwind + router + axios token refresh | Frontend | M |
| 0.6 | Register/login + role-aware onboarding | Frontend | M |
| 0.7 | CI workflow + git init + .gitignore | Platform | S |

### Phase 1
| # | Task | WS | Size |
| - | ---- | -- | ---- |
| 1.1 | Profiles + addresses (country-aware) | Backend | M |
| 1.2 | Businesses + imports-from/exports-to countries | Backend | M |
| 1.3 | Business documents upload (IFileStorage) | Backend | M |
| 1.4 | Verification workflow + admin queue + Verified badge | Backend+Admin | L |
| 1.5 | Categories + commodity/HS-code helper seed | Backend | S |
| 1.6 | Export product listings (trade terms, HS code, ports) | Backend | L |
| 1.7 | Marketplace search by origin/destination/commodity/verified | Backend+Frontend | L |
| 1.8 | Product detail (trade terms panel, Request Quote) | Frontend | M |
| 1.9 | RFQ publish/expiry + open directory | Backend | M |
| 1.10 | Quotation create/compare (trade terms) + negotiation thread | Backend+Frontend | L |
| 1.11 | Messaging + notifications | Backend+Frontend | M |
| 1.12 | Seed demo data (2 corridors, verified exporters) | Platform | M |

### Phase 2
| # | Task | WS | Size |
| - | ---- | -- | ---- |
| 2.1 | Order model w/ incoterms, ports, payment terms, currency + handoff | Backend | L |
| 2.2 | Order lifecycle + inventory reserve + buyer/seller inboxes | Backend+Frontend | L |
| 2.3 | Trade document upload + statuses + admin verification queue | Backend+Admin | L |
| 2.4 | Payment gateway adapter #1 (sandbox) + idempotent webhook | Backend | L |
| 2.5 | Off-platform payment recording + transaction ledger | Backend | M |
| 2.6 | Shipment record + tracking events + guest tracking | Backend+Frontend | L |
| 2.7 | Admin dashboard (all queues + KPIs) | Admin | L |

---

## Definition of Done (per phase)

- All stories closed with tests for backend logic + key API contracts (integration tests
  against local SQL Server).
- No open high/critical security findings (auth, authorization, input validation).
- Seed data present; demo journey documented (see `05-mvp-scope.md`).
- Frontend wired to real API on the milestone flow.
- Phase exit criteria (above) demonstrably met.