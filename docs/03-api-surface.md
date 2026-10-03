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
| GET | `/orders/{id}` | Participant | Detail incl. items, trade terms, payments, shipment summary |
| POST | `/orders/{id}/cancel` | Buyer/Seller | While Pending/Confirmed; releases inventory |
| PATCH | `/orders/{id}/status` | Seller | Confirmed → Processing → InTransit → Delivered |
| POST | `/orders/{id}/complete` | Buyer | Confirm arrival → Completed (enables reviews) |
| GET | `/admin/orders` | Admin | Global list; filters corridor/currency/status |
| PATCH | `/admin/orders/{id}/status` | Admin | Corrective moves |

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
| POST | `/shipments` | Seller or LSP | Create shipment for an order (mode, ports, carrier, ETA) |
| GET | `/shipments` | User | `?as=seller\|buyer\|carrier&status=` |
| GET | `/shipments/{id}` | Participant | Detail + tracking timeline |
| POST | `/shipments/{id}/tracking-events` | Carrier/Seller | Append event (status, location, description) |
| GET | `/shipments/track/{publicTrackingToken}` | Public | Guest tracking page data |
| GET | `/admin/shipments` | Admin | All shipments |

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

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/admin/users` · PATCH `/admin/users/{id}/status` | Admin | Directory + suspend/activate |
| GET | `/admin/businesses` | Admin | `?status=verified\|pending…` |
| GET | `/admin/rfqs` | Admin | All RFQs + volume filters |
| GET | `/admin/transactions` | Admin | Ledger + reconciliation |
| GET | `/admin/reports/{kind}` | Admin | `corridors\|rfq\|verification-funnel\|commodities`, `?from=&to=` |
| GET/PUT | `/admin/settings` | Admin | Default currency list per corridor, verification levels, feature flags |
| GET | `/admin/audit-log` | Admin | Audit trail (spec §18) |

## Not in scope until noted

Domestic buy-now/cart endpoints (Phase 4), logistics-provider *directory/booking*
marketplace (Phase 4), escrow, AI, price-feed ingestion API (internal job), and
customs-service integrations (Phase 5).

## Realtime model

- **Notifications hub** (`/hubs/notifications`, bearer): push on RFQ/quote/order/
  document/payment/shipment/verification events.
- **Messages hub** (`/hubs/messages`): join per `conversationKey`; `Send`/`Read` events.
- Fallback: poll `GET /me/notifications?unreadOnly=true` every 30s.