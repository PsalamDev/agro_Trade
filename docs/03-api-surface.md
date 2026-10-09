# 03 — API Surface

Versioned REST API for the **cross-border import/export platform**. Swagger/OpenAPI at
`/swagger`; all endpoints under `/api/v1`.

## Conventions

| Concern | Rule |
| --- | --- |
| Versioning | `/api/v1/...` (URL segment); v2+ additive, breaking changes bump version |
| Auth | `Authorization: Bearer <jwt>`; 401 unauth, 403 forbidden-for-role |
| Role classes | **Public**, **User** (any authenticated), **Exporter/Seller** (Farmer/Supplier/AgroProcessor/Exporter/Distributor), **Importer/Buyer** (Buyer/Importer/Distributor), **LSP** (LogisticsProvider), **Admin** |
| Policies | `Administrator`, `VerifiedSeller` (business Verified), `SellerOrAdmin`, `BuyerOrAdmin`; ownership enforced in services (not just roles) |
| Pagination | `?page=1&pageSize=20` (max 100) → envelope `{ items, page, pageSize, total, totalPages }` |
| Sorting | `?sortBy=<field>&sortDir=asc\|desc` (allowlist per resource) |
| Filtering | Query params per resource; search = `q` |
| Errors | ProblemDetails `{ type, title, status, code, message, fieldErrors? }`; `code` machine-readable (`VALIDATION_ERROR`, `NOT_FOUND`, `QUOTATION_CLOSED`, `INVENTORY_INSUFFICIENT`) |
| Idempotency | Caller-supplied `idempotencyKey` in the request **body** on POST `/orders/{id}/payments` and POST `/orders/{id}/payments/manual`; replay returns the original resource. Off-platform payments are additionally guarded by the bank's remittance reference, which is unique per order — so a genuine double-entry is caught whether or not a key was sent. |
| Countries | ISO-3166 alpha-2 codes everywhere (`ng`, `KE`, `cn`) |
| Currencies | ISO-4217 codes; price/quote/order amounts always carry a currency |
| File uploads | `multipart/form-data`, ≤ 10 MB, allowlisted MIME types |
| Realtime | SignalR hubs `/hubs/notifications`, `/hubs/messages` (JWT); polling fallback |

---

## Auth & Identity

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/auth/register` | Public | Create user + roles (Exporter/Importer/Seller/Farmer/…) → email confirm |
| POST | `/auth/login` | Public | `accessToken`, `refreshToken`, user summary + roles |
| POST | `/auth/refresh` | Public | Rotate refresh token (reuse detection) |
| POST | `/auth/logout` | User | Revoke refresh token |
| POST | `/auth/forgot-password` / `/auth/reset-password` | Public | |
| GET | `/auth/me` | User | Profile, roles, businesses, verification status, trade countries |
| PUT | `/me` | User | Update profile/currency/home country |
| GET/PUT | `/me/addresses` · DELETE `/me/addresses/{id}` | User | Address book; one primary |

## Businesses

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/businesses` | User | Register business (legal/trading/country/type) → Unverified |
| GET | `/businesses` | Public | Filters `country`, `type`, `verifiedOnly`, `exportsTo`, `importsFrom`, `q`; pagination |
| GET | `/businesses/{id}` | Public | Profile: badge, rating, reviews, trade countries, active export listings |
| PUT | `/businesses/{id}` | Owner | Update profile |
| PUT | `/businesses/{id}/trade-countries` | Owner | Set exports-to / imports-from country lists (replaces per direction) |
| POST | `/businesses/{id}/documents` | Owner | Upload KYC doc (registration, tax, ID, proof of address) |
| GET | `/businesses/{id}/documents` | Owner/Admin | |
| POST | `/businesses/{id}/verification-submissions` | Owner | Submit → Pending |
| GET | `/businesses/{id}/verification` | Owner/Admin | Status + history |
| GET | `/businesses/{id}/reviews` | Public | Reviews + aggregate |
| POST | `/admin/businesses/{id}/verification/review` | Admin | Approve/Reject with notes → Verified badge |

## Categories & commodities

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/categories` | Public | Nested tree |
| GET | `/commodities/hs-codes` | Public | HS-code starter map by commodity |
| POST / PUT / DELETE | `/admin/categories` | Admin | CRUD; slug unique |

## Products (export listings)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/products` | Public | Search: `q, categoryId, hsCode, originCountry, exportsToCountry, incoterms, currency, minPrice, maxPrice, verifiedOnly, page, pageSize, sortBy` |
| GET | `/products/{idOrSlug}` | Public | Detail incl. trade-terms panel (incoterms, lead time, port of loading, packaging, certifications) |
| POST | `/products` | Seller | Create draft listing (origin country, HS code, trade terms) |
| PUT | `/products/{id}` | Owner | Update; Draft/Active only |
| DELETE | `/products/{id}` | Owner | Soft delete |
| POST | `/products/{id}/images` · PUT/DELETE `/products/{id}/images/{imageId}` | Owner | |
| PUT | `/products/{id}/inventory` | Owner | Reconcile stock (`RowVersion` guarded) |
| POST | `/products/{id}/locations` | Owner | Dispatch locations |
| PATCH | `/admin/products/{id}/moderation` | Admin | Unpublish/suspend listing |

## RFQs / Trade Requests (importer sourcing)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/rfqs` | Buyer | Publish sourcing request: destination country + port, preferred incoterm, transport mode, quantity/unit, specs, required certifications, preferred currency + payment terms, deadline |
| GET | `/rfqs/open` | Public | Open RFQs directory (sanitized: no budget/PII), filter by `destinationCountry`, `categoryId` |
| GET | `/rfqs` | User | `?as=buyer\|supplier&status=` |
| GET | `/rfqs/{id}` | Participant | Full RFQ |
| POST | `/rfqs/{id}/quotations` | Seller (Verified) | Quote with trade terms (incoterm, ports, lead time, payment terms, price/currency, validity) |
| GET | `/rfqs/{id}/quotations` | Buyer | Comparison view (side-by-side trade terms) |
| GET | `/quotations/{id}` | Participant | Quote detail |
| POST | `/quotations/{id}/accept` | Buyer | Accept → order created from quotation (all trade terms carried over) |
| POST | `/quotations/{id}/withdraw` | Seller | Before acceptance |
| GET/POST | `/rfqs/{id}/messages` | Participant | Negotiation thread |

## Orders (cross-border)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/orders` | Buyer | Create from `acceptedQuotationId` (preferred) or direct item entry; **Idempotency-Key required**; sets incoterms, ports, payment terms, currency, transport mode |
| GET | `/orders` | User | `?as=buyer\|seller&status=&currency=&page=` |
| GET | `/orders/{id}` | Participant | Detail incl. items, trade terms, payments |
| POST | `/orders/{id}/cancel` | Buyer/Seller | While Pending/Confirmed; releases inventory |
| PATCH | `/orders/{id}/status` | Seller | Confirmed → Processing → InTransit → Delivered |
| POST | `/orders/{id}/complete` | Buyer | Confirm arrival → Completed (enables reviews) |
| GET | `/admin/orders` | Admin | Global list; `?status=&paymentStatus=&q=` — implemented task 2.7, see [Admin](#admin) |
| PATCH | `/admin/orders/{id}/status` | Admin | Corrective moves — **not implemented**. The `OrderLifecycle` state machine from task 2.2 is currently the only authority on order status, and an admin override would be a second one. |

## Payments & Transactions

Implemented through task 2.5. Routes are nested under the order they belong to, so a payment or a
ledger entry cannot be addressed without naming the order it settles.

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/orders/{id}/payments` | Buyer | Initiate **gateway** payment; idempotent via body `idempotencyKey`; returns reference + checkout URL |
| POST | `/orders/{id}/payments/manual` | **Seller** | **Record** a settled bank transfer or L/C; requires the bank's remittance reference |
| GET | `/orders/{id}/payments` | Participant | Payments on an order |
| GET | `/orders/{id}/transactions` | Participant | The order's ledger, oldest first — one immutable row per movement of money |
| GET | `/payments/{id}` | Participant/Admin | Status |
| POST | `/payments/webhooks/{provider}` | Public(secret) | Provider callback; signature verified; replay-safe; updates payment + order |
| GET | `/admin/payments` | Admin | All payments; filters |
| POST | `/admin/payments/{id}/refund` | Admin/Finance | Refund workflow (not started) |

## Invoices & Receipts

Implemented in Phase 3 as the document pair around a payment: the invoice is what the buyer is
asked to pay, and the receipt is the evidence the money arrived. Both are scoped to the order.

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/orders/{id}/invoice` | **Seller**/Admin | Issue the order's invoice for its total; one per order |
| GET | `/orders/{id}/invoice` | Participant | The order's invoice, once issued |
| POST | `/orders/{id}/payments/{paymentId}/receipt` | Participant | Issue (or re-read) the receipt for a settled payment |
| GET | `/orders/{id}/receipts` | Participant | Receipts for the order, newest first |

A receipt is written **automatically** by the settlement path �?" whether the money arrived through a
gateway webhook or a seller-recorded manual transfer �?" so it is produced once per settled payment
and cannot be missed. The `POST` above is idempotent and exists only so a client can retrieve the
document on demand. A receipt is refused while the payment is unsettled, because a receipt attests
that money arrived; a fully-paid order also closes its invoice.

Two deliberate departures from the original plan above:

- **`/payments/off-platform` became `/orders/{id}/payments/manual`, and the actor changed from
  buyer to seller.** The buyer sends the money, but only the beneficiary of the account can observe
  it arriving, so the seller is the only party that can honestly record a settlement. Buyer
  self-attestation would let anyone mark an order paid and demand dispatch against funds the
  platform cannot verify.
- **`/me/transactions` is served per order for now.** A cross-order ledger is an admin and
  reporting concern; the order-scoped read is what both parties actually need, to reconcile a
  statement against a specific trade. A consolidated view will follow it.

A manual payment is recorded as already `Paid` rather than pending — there is no provider to poll —
and the bank's remittance reference is required and unique per order. It is the only externally
checkable detail on a record nobody witnessed, and it is what makes a double-entry detectable.
Proof-document upload is not part of this task; see `/orders/{id}/documents` in Trade Documents,
which is order-scoped and already carries the trade paperwork.

Idempotency is a request-body field rather than an `Idempotency-Key` header, so that the key is
visible in the same payload as the amount it protects.

## Trade Documents

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/documents` | Participant | Upload trade doc (DocType: invoice, packing list, COO, B/L, phytosanitary, inspection, permits) with `ContextType`/`ContextId` |
| GET | `/documents` | User | `?contextType=&contextId=&docType=` |
| GET | `/documents/{id}` | Authorized | Metadata + signed URL |
| POST | `/documents/{id}/submit` | Uploader | Uploaded → UnderReview |
| POST | `/admin/documents/{id}/verify` | Admin | Approved/Rejected with notes; sets verified status |
| GET | `/admin/documents` | Admin | Review queue `?status=pending&docType=` |

## Shipments & Tracking

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/orders/{id}/shipments` | Seller | Book a consignment. Refused on a draft or cancelled order (`ORDER_CLOSED`), and on an order that already has one. Ports and incoterm default to the order's own. Writes milestone #0, and issues a guest token unless `enableGuestTracking: false` |
| GET | `/orders/{id}/shipments` | Participant | The order's consignments. A stranger gets 404, not an empty list, so the endpoint cannot be used to probe for orders |
| GET | `/shipments` | User | `?as=seller\|buyer&status=` — the caller's own consignments across orders. The derived status filter runs after load, because status is read off the timeline rather than stored |
| GET | `/shipments/{id}` | Participant | Detail + full milestone timeline, newest first |
| POST | `/shipments/{id}/tracking-events` | Seller | Append a milestone. Permanent — there is no update or delete. Rejects a future date (`MILESTONE_IN_FUTURE`) and normalises offset timestamps to UTC |
| POST | `/shipments/{id}/tracking-token` | Seller | Issue a fresh guest token, killing the previous link. Returns the raw token — the only time it is ever visible, at creation or rotation |
| GET | `/shipments/track/{token}` | **Anonymous** | Guest tracking. A separate reduced payload (`ShipmentPublicTrackingDto`): route, carrier, tracking reference, mode, ETA, incoterm, status, timeline. No order, parties, goods, documents or money. 404 for anything unrecognised, so a valid token cannot be distinguished from an invalid one |

Nothing under `/shipments/{id}` discloses the consignment to a stranger: they get 404, and a
buyer attempting a seller action gets 403 — they already know the order is theirs.

The guest endpoint lives on its own `[AllowAnonymous]` controller, and the public payload is a
distinct DTO rather than `ShipmentDto` with fields hidden. That is the point of it: the frontend
cannot leak what it was never sent, and the narrow shape is enforced by the compiler rather than
by remembering to redact.

## Messaging & Notifications

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/messages/conversations` | User | Threads + last message + unread count |
| GET | `/messages/conversations/{participantId}` | User | History (paginated) |
| POST | `/messages` | User | Send (context-aware) |
| PATCH | `/messages/read` | User | Mark thread read |
| GET | `/me/notifications` | User | Paginated, `?unreadOnly=` |
| PATCH | `/me/notifications/{id}/read` · `read-all` | User | |

## Reviews & Disputes

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/reviews` | Buyer/Seller | On Completed order; one per direction (Phase 3) |
| POST | `/disputes` | Participant | Raise on order |
| GET | `/disputes/{id}` | Participant/Admin | |
| GET | `/admin/disputes` · POST `/admin/disputes/{id}/resolve` | Admin | Queue + resolution |

## Market Intelligence (Phase 3)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/market/prices` | Public | `?commodity=&country=&from=&to=&currency=` series |
| GET | `/market/trends` | Public | Trend indicators |
| GET | `/market/commodities` | Public | Commodity list + latest price |
| GET | `/market/destination-requirements` | Public | Certs/permits by origin/destination (seeded content) |
| GET | `/market/reports/{slug}` | Public | Content-managed report |

## Admin

Implemented in tasks 2.3 and 2.7. Every route below is live; the remaining planned
administrative surface is listed separately so the two are never confused.

| Method | Route | Description |
| --- | --- | --- |
| GET | `/admin/overview` | **One request, the whole dashboard.** Platform counts, four funnels (business verification, document verification, order status, order payment status), corridor value, per-category supply vs demand, RFQ demand. |
| GET | `/admin/audit-log` | Administrator decisions, newest first. `?action=&userId=&q=` |
| GET | `/admin/users` | User directory. `?q=&role=&emailConfirmed=&lockedOut=`. `role` is a role **name**, not an id — ids are database-generated and undiscoverable through the API, so an id filter would be a parameter no client could fill. |
| GET | `/admin/orders` | Global order queue. `?status=&paymentStatus=&q=` |
| GET | `/admin/shipments` | Global consignment queue. `?status=&q=&hasGuestTracking=` |
| GET | `/admin/businesses` | Business queue. `?status=&q=` |
| POST | `/admin/businesses/{id}/verification/review` | Approve/reject with notes; sets the verified badge. Writes an audit entry. |
| GET | `/admin/documents` | Document review queue. `?status=&docType=&q=` |
| POST | `/admin/documents/{id}/review` | Approve/reject with notes. Writes an audit entry. |

### Notes on the dashboard contract

`GET /admin/overview` is deliberately **one endpoint rather than eight**. A dashboard is one
screen; splitting it would mean eight round trips and eight loading states, and the tiles would
render against each other only by accident. It is not cached or precomputed — it is eight
sequential aggregate queries, because EF forbids concurrent operations on a single scoped
`DbContext`, so `Task.WhenAll` here returns 500 under load.

Three things the response deliberately refuses to do:

- **Corridors are keyed by `(PortOfLoading, DestinationCountry, Currency)`.** There is no FX rate
  anywhere in the system, so a corridor total that added NGN to USD would be a sum of
  incomparable numbers. Rows are uncapped for the same reason the commodity panel is capped:
  the corridor an administrator opened the page to inspect is often the low-volume one, and
  silently omitting it reads as "no shipments".
- **Corridor rows report gross order value *and* net settled payments side by side.** They
  answer different questions, and collapsing them hides unpaid orders — a corridor can look
  healthy on gross volume while nothing has been paid. Net settled comes from the transaction
  ledger rather than the order's payment rollup; a ledger entry denominated differently from its
  order forms its own row and is reported as `mismatchedLedgerEntries` rather than distorting a
  total.
- **Status labels are not in the payloads.** `AdminService` supplies them, so a status enum never
  leaks into a client contract. The one exception is `AdminFunnelStageDto.label`, which is
  deliberate: a funnel stage with no name is not renderable.

Funnels count **orders** by payment status (not payments by payment status) and return every
enum stage, including empty ones — an omitted stage is indistinguishable from a bug, a rendered
zero is information. The commodity panel is driven by the **union** of listed and RFQ'd
categories, so a category buyers ask for and nobody lists appears; each row carries `rfqCount`
(lifetime interest) and `openRfqCount` (live demand) separately, and the gap flag keys off the
latter. `demand.preferredCurrency` has three states — the agreed currency, `"Mixed"` when open
RFQs genuinely disagree, and empty when none are open.

`AdminShipmentDto.status` is derived from the latest tracking event, as in task 2.6, and the
`status` filter is therefore evaluated in memory against the loaded event rather than in SQL.
`hasGuestTracking` answers "which consignments have a link in circulation" — a guest token is a
bearer secret exposed to non-accounts, which makes that a real question for an operator to be
able to ask.

## Admin — planned, not yet built

The Phase 2 scope in `01-roadmap.md` names seven admin queues: users, businesses,
**products**, **RFQs**, orders, documents, shipments. Five exist. The two missing ones are
below, and are the reason Phase 2 is not yet fully closed despite every numbered task landing.

| Method | Route | Description |
| --- | --- | --- |
| GET | `/admin/products` | Product moderation queue — listings across every seller, filterable by status/category |
| PATCH | `/admin/products/{id}/moderation` | Unpublish/suspend a listing |
| GET | `/admin/rfqs` | All RFQs + volume filters |
| GET | `/admin/transactions` | Ledger + reconciliation |
| GET | `/admin/reports/{kind}` | `corridors\|rfq\|verification-funnel\|commodities`, `?from=&to=` |
| GET/PUT | `/admin/settings` | Default currency list per corridor, verification levels, feature flags |
| PATCH | `/admin/users/{id}/status` | Directory + suspend/activate. Users are **not** hard-deleted — `AuditLog.UserId` is `ON DELETE RESTRICT` precisely so that removing an account cannot remove the evidence that it approved a transaction. |

`GET /admin/audit-log` was previously listed here as planned. It is now implemented, above.

## Not in scope until noted

Domestic buy-now/cart endpoints (Phase 4), logistics-provider *directory/booking*
marketplace (Phase 4), escrow, AI, price-feed ingestion API (internal job), and
customs-service integrations (Phase 5).

## Realtime model

- **Notifications hub** (`/hubs/notifications`, bearer): push on RFQ/quote/order/
  document/payment/shipment/verification events.
- **Messages hub** (`/hubs/messages`): join per `conversationKey`; `Send`/`Read` events.
- Fallback: poll `GET /me/notifications?unreadOnly=true` every 30s.