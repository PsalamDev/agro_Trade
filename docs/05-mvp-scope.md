# 05 — MVP Scope

Single source of truth for the **cross-border MVP** (= Phases 0–2 of the roadmap).
It delivers spec section 13 **around import & export** — the platform's core value —
and nothing more.

**MVP core trade loop:**
`Discover exporters/listings → Source via RFQ (with destination + incoterm) → Verify businesses & docs → Negotiate → Order (trade terms) → Documents → Payment → Shipment tracking`

## MVP = Phases 0–2

| Spec §13 item | Build deliverables (cross-border flavored) | Acceptance criteria |
| --- | --- | --- |
| User registration and login | Identity, JWT + refresh, email confirm, forgot/reset | Register → confirm → login → token works; reset works |
| Role-based accounts | 9 roles incl. `Exporter`/`Importer`; multi-role; guards + policies | A `Farmer`+`Exporter` sees both views; importer gets 403 on exporter endpoints |
| Business profiles | Business CRUD, country, type, **trade countries** (exports-to/imports-from), public profile, locations, addresses | Business page renders trade directions + verification status; owner can edit |
| Business verification workflow | Doc uploads (reg/tax/ID), submit-for-verification, admin queue, approve/reject w/ notes, **Verified badge** | Unverified → Pending → Verified; badge appears in search + profile; rejected returns with reason |
| Product listing and management | **Export listings**: origin country, HS code, price + currency, MOQ, incoterms, lead time, port of loading, packaging, certifications, images, stock; seller listing screen | Exporter creates a full export listing; appears in search when Active; soft-delete works |
| Marketplace search and filtering | `q, categoryId, hsCode, originCountry, exportsToCountry, incoterms, currency, price range, verifiedOnly, sort, paging` | Destination-reachability filter returns only exporters trading to that country; verified-only excludes unverified |
| Product details | Trade-terms panel, certifications, seller card w/ badge, `Request Quote` / `Contact Seller` | All spec §7 listing data rendered; CTA states correct |
| RFQ / Request Quote | Publish sourcing request (destination country + port, preferred incoterm, transport mode, specs, required certs, preferred currency, deadline), open-RFQ directory, exporter quotations with trade terms, comparison, negotiation thread, **accept → order** | Importer publishes → 2 exporters quote w/ trade terms → compare → negotiate → accept → order created losslessly |
| Buyer–supplier messaging | Conversations, threads, unread, read receipts, SignalR | Real-time both directions with unread badge |
| Basic order management | Order from accepted quote (or manual), **incoterms/ports/payment terms/currency**, lifecycle, inventory reserve/release, buyer+exporter lists | Trade terms preserved through the whole order; cancel releases stock; concurrency-safe |
| Basic notifications | In-app + unread badge on RFQ/quote/order/document/payment/shipment events | Event triggers notification; mark-read persists |
| Basic payment integration | First gateway adapter (sandbox, one corridor) + **off-platform payment recording** (bank transfer/L/C) → transaction ledger; idempotent webhook | Gateway payment and recorded transfer both settle the order exactly once; replay-safe |
| Basic document management | **Trade documents**: invoice, packing list, COO, B/L, phytosanitary, inspection, permits — upload, submit, admin verify/reject + signed URL | Doc goes Uploaded → UnderReview → Verified/Rejected; admin queue driven |
| *(MVP add-on)* | Basic shipment tracking (shipment record, tracking events, guest tracking token) | Shipment appended event visible to buyer; guest link works |

## Explicitly out of scope for MVP (parking lot)

- Domestic buy-now / cart / retail marketplace (Phase 4)
- Escrow / escrow-style transactions (needs regulated partner — legal dependency)
- Full multi-currency **gateway matrix** (MVP executes one gateway corridor + recorded
  transfers; data model + ledger already multi-currency)
- Logistics-provider *directory and booking* marketplace (Phase 4; shipment + tracking
  on an order IS in MVP)
- Reviews & ratings, disputes resolution UI (Phase 3)
- Market intelligence pages and price feeds (Phase 3)
- AI features, forecasts, trade-risk scoring (Phase 5)
- Customs-system integrations (Phase 5)
- Mobile app (Phase 5)
- SMS (email + in-app only)
- Public APIs/webhooks for third-party integrators
- Enterprise accounts / white-labeling

## First sprint backlog (ordered — start after Phase 0)

| # | Story | Size |
| - | ---- | :--: |
| 1 | Seed roles + administrator + categories + HS-code starter map | S |
| 2 | `InitialCreate` migration applies cleanly to local SQL Server | S |
| 3 | Register/login/refresh + email confirm + role policies | M |
| 4 | `/auth/me` + profile + country-aware addresses | M |
| 5 | Business create/update/detail + **trade countries** + search (incl. `exportsTo`) | L |
| 6 | Business documents upload + submit-for-verification | M |
| 7 | Admin verification queue approve/reject → Verified badge | L |
| 8 | **Export listing** CRUD: origin country, HS code, trade terms, images, stock | L |
| 9 | Marketplace search/filter/paginate/sort + product detail (trade terms) | L |
| 10 | RFQ publish (destination, incoterm, certs, currency) + open directory | M |
| 11 | Quotation create/compare (trade terms) + negotiation thread | L |
| 12 | Messaging + notifications + unread badges | M |
| 13 | Accept quotation → **order handoff** (trade terms preserved) + lifecycle | L |
| 14 | Payment gateway adapter #1 (sandbox) + idempotent webhook + **off-platform record** + ledger | L |
| 15 | Trade document upload/submit + admin verify queue | L |
| 16 | Shipment record + tracking events + guest tracking | L |
| 17 | Admin dashboard (KPIs + all queues) | L |
| 18 | Seed demo corridors + documented demo journey | M |

**S** < 1 dev-day · **M** 1–2 dev-days · **L** 3–5 dev-days (single developer).

## Definition of Done (per story)

Code merged on a PR with tests · Swagger updated · no new warnings · frontend wired to
real API · acceptance criteria + one happy path walked from seed data · no scope creep.

## Demo journey (after MVP)

1. Admin verifies **Uzuri Exporters Ltd (Kenya)**, a cashew exporter trading FOB Mombasa
   (`exportsTo: NG, KE, EU`).
2. **Delta Foods (Nigeria, importer)** registers, searches "cashew" with
   `originCountry: KE` → sees Uzuri's Verified listing (HS 0801, FOB, 14-day lead).
3. Delta publishes an RFQ: 5 MT, destination Lagos (LOS), preferred incoterm CIF,
   phytosanitary required, USD.
4. Uzuri + a second exporter quote; Delta compares trade terms side-by-side, negotiates
   in-thread, accepts Uzuri's CIF quote.
5. Order auto-created with incoterms, ports, payment terms, currency preserved.
6. Uzuri uploads invoice + packing list + COO + phytosanitary; admin verifies.
7. Delta initiates gateway payment (or records a T/T transfer reference); payment ledger
   settles the order.
8. Shipment created (sea, Mombasa → Lagos), tracking events visible; guest tracking link
   shared with the consignee.