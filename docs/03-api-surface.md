# 03 — API Surface

Versioned REST API for the **cross-border import/export platform**. Swagger/OpenAPI at
`/swagger`; all endpoints under `/api/v1`.

## Conventions

| Concern | Rule |
| --- | --- |
| Versioning | `/api/v1/...` (URL segment); v2+ additive, breaking changes bump version |
| Auth | `Authorization: Bearer <jwt>`; 401 unauth, 403 forbidden-for-role |
| Role classes | **Public**, **User** (any authenticated), **Exporter/Seller** (Farmer/Supplier/AgroProcessor/Exporter/Distributor), **Importer/Buyer** (Buyer/Importer/Distributor), **LSP** (LogisticsProvider), **Admin** |
| Policies | `Administrator`, `VerifiedSeller` (business Verified), `SellerOrAdmin`, `BuyerOrAdmin`, `Buyer` (Buyer/Importer/Distributor — cart, Phase 4), `Lsp` (LogisticsProvider — provider profile + booking responses); ownership enforced in services (not just roles) |
| Pagination | `?page=1&pageSize=20` (max 100) → envelope `{ items, page, pageSize, total, totalPages }` |
| Sorting | `?sortBy=<field>&sortDir=asc\|desc` (allowlist per resource) |
| Filtering | Query params per resource; search = `q` |
| Errors | ProblemDetails `{ type, title, status, code, message, fieldErrors? }`; `code` machine-readable (`VALIDATION_ERROR`, `NOT_FOUND`, `QUOTATION_CLOSED`, `INVENTORY_INSUFFICIENT`, `CART_EMPTY`, `PRODUCT_UNAVAILABLE`, `DELIVERY_COUNTRY_MISMATCH`) |
| Idempotency | Caller-supplied `idempotencyKey` in the request **body** on POST `/orders/{id}/payments` and POST `/orders/{id}/payments/manual`; replay returns the original resource. Off-platform payments are additionally guarded by the bank's remittance reference, which is unique per order — so a genuine double-entry is caught whether or not a key was sent. |
| Countries | ISO-3166 alpha-2 codes everywhere (`ng`, `KE`, `cn`) |
| Currencies | ISO-4217 codes; price/quote/order amounts always carry a currency |
| Timestamps | ISO-8601 in UTC and always ending in `Z`, so every `*Utc` field is an unambiguous instant a client cannot misread as local |
| File uploads | `multipart/form-data`, ≤ 10 MB, allowlisted MIME types |
| Realtime | SignalR hubs `/hubs/notifications`, `/hubs/messages` (JWT); polling fallback |

---

## Auth & Identity

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/auth/register` | Public | Create user + roles. `roles` selects the onboarding path — domestic `Buyer` (buy locally, no verification) or international `Importer`/`Distributor` (business verification follows). Filtered to `RoleNames.Registrable`; defaults to `Buyer` → email confirm. A buyer account number (`buyerId`, e.g. `BUY-ID-AG-000001`) is assigned at registration under a locked sequence and returned on the user summary |
| POST | `/auth/login` | Public | `accessToken`, `refreshToken`, user summary + roles |
| POST | `/auth/refresh` | Public | Rotate refresh token (reuse detection) |
| POST | `/auth/logout` | User | Revoke refresh token |
| POST | `/auth/forgot-password` / `/auth/reset-password` | Public | |
| GET | `/auth/me` | User | Profile, roles, businesses, verification status, trade countries, `buyerId` |
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
| GET | `/countries` | Public | Full ISO 3166-1 list: name + alpha-2/alpha-3/numeric (powers the searchable country pickers; the UI stores/submits the alpha-2 code) |
| POST / PUT / DELETE | `/admin/categories` | Admin | CRUD; slug unique |

## Products (export listings)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/products` | Public | Search: `q, categoryId, hsCode, originCountry, exportsToCountry, incoterms, currency, minPrice, maxPrice, verifiedOnly, domesticOnly, page, pageSize, sortBy` |
| GET | `/products/{idOrSlug}` | Public | Detail incl. trade-terms panel (incoterms, lead time, port of loading, packaging, certifications) |
| POST | `/products` | Seller | Create draft listing (origin country, HS code, trade terms) |
| PUT | `/products/{id}` | Owner | Update; Draft/Active only |
| DELETE | `/products/{id}` | Owner | Soft delete |
| POST | `/products/{id}/images` · PUT/DELETE `/products/{id}/images/{imageId}` | Owner | |
| PUT | `/products/{id}/inventory` | Owner | Reconcile stock (`RowVersion` guarded) |
| POST | `/products/{id}/locations` | Owner | Dispatch locations |
| PATCH | `/admin/products/{id}/moderation` | Admin | Unpublish/suspend listing |

Since Phase 4, product DTOs carry `sellsDomestically` and `domesticDeliveryFee` (the flat per-line
domestic delivery charge, in the listing's currency). `CreateProductRequest`/`UpdateProductRequest`
accept both. The marketplace search accepts `domesticOnly=true` to restrict results to the buy-now
listings that ship locally (the domestic onboarding path's default view); domestic availability is
otherwise resolved per listing (seeded on the four Nigerian catalogue items).

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
| GET | `/orders/{id}` | Participant | Detail incl. items, trade terms, payments, and — on domestic orders — the snapshotted delivery address (`deliveryLine1`…`deliveryPostalCode`, country `destinationCountry`) |
| POST | `/orders/{id}/cancel` | Buyer/Seller | While Pending/Confirmed; releases inventory |
| PATCH | `/orders/{id}/status` | Seller | Confirmed → Processing → InTransit → Delivered |
| POST | `/orders/{id}/complete` | Buyer | Confirm arrival → Completed (enables reviews) |
| GET | `/admin/orders` | Admin | Global list; `?status=&paymentStatus=&q=` — implemented task 2.7, see [Admin](#admin) |
| PATCH | `/admin/orders/{id}/status` | Admin | Corrective moves — **not implemented**. The `OrderLifecycle` state machine from task 2.2 is currently the only authority on order status, and an admin override would be a second one. |

## Cart & domestic buy-now (Phase 4 — Slice 1)

Domestic-only: a seller sets `sellsDomestically` (with a flat `domesticDeliveryFee`) on the listing
form; the seeder does this for the four Nigerian catalogue listings. Any Buyer/Importer/Distributor
can add such listings to a cart that lives **server-side** — the client only ever holds product
ids and quantities, and every read resolves the live listing terms (price, currency, delivery
fee, stock, domestic availability). Checkout fans the cart out into one `Order` per seller on
the existing orders model (DAP/Road, `Placed`), so there is no new order sub-state; MOQ is not
enforced domestically.

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/carts/mine` | Buyer | The caller's cart with live pricing; an empty cart is `{ items: [], totalQuantity: 0 }` |
| POST | `/carts/items` | Buyer | `{ productId, quantity? }` — adds, or increments an existing line. Product must exist (404 `PRODUCT_NOT_FOUND`) and be active/domestic and not the caller's own listing |
| PUT | `/carts/items/{productId}` | Buyer | `{ quantity }` absolute (≥ 1); 404 `CART_ITEM_NOT_FOUND` if the line is absent; exceeds available (409 `QUANTITY_EXCEEDS_AVAILABLE`) or non-domestic (400) are refused |
| DELETE | `/carts/items/{productId}` | Buyer | Remove the line; **idempotent** — a missing line is not an error |
| POST | `/carts/checkout` | Buyer | `{ destination, notes? }` — re-validates every line, reserves stock across the whole cart atomically, splits into one `Order` per seller (`DestinationCountry` = seller origin, `ExpectedDeliveryUtc` = now + max lead, `DeliveryFee` = Σ line fees, each order **snapshots** the delivery address as `deliveryLine1`…`deliveryPostalCode` with the country in `DestinationCountry` — it writes no address-book row and leaves `DestinationAddressId` null), empties the cart, returns the created order summaries |

Checkout errors: `CART_EMPTY`, `PRODUCT_NOT_FOUND` (404), `PRODUCT_UNAVAILABLE` (the line's listing
changed after it was added — taken down, deactivated, suspended, short of stock), `PRODUCT_NOT_DOMESTIC`,
`ORDER_SELF_SALE`, `CURRENCY_MIXED`, `DELIVERY_COUNTRY_MISMATCH` (address country ≠ seller origin —
"domestic" is fixed by this check), `QUANTITY_EXCEEDS_AVAILABLE`, `INSUFFICIENT_STOCK` (409).

## Logistics directory & booking (Phase 4 — Slice 3)

A logistics provider is a user with the `LogisticsProvider` role who has published a profile. The
directory is **public** and carries no contact details; the full profile (contact details included)
requires an account, and only the owner may write a profile. Provider profiles are seeded/
operator-onboarded, not self-registered — `LogisticsProvider` is not in `RoleNames.Registrable`.

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/logistics/providers` | Public | Directory search; `?country=&mode=&service=&q=&page=&pageSize=`. An unrecognised `mode`/`service` is a 400 `INVALID_FILTER`, never a silent "any". Card shape carries **no contact details** |
| GET | `/logistics/providers/mine` | LSP | The caller's own profile, or 404 `PROVIDER_NOT_FOUND` |
| PUT | `/logistics/providers/mine` | LSP | Create or **replace** the caller's profile — whole-object, not a patch. Requires `companyName`, an alpha-2 `homeCountry`, ≥ 1 `modes`, ≥ 1 `coverage` country; optional contact fields and `isActive` |
| GET | `/logistics/providers/{id}` | User | Full profile including contact details; `isMine` tells the UI whether to offer editing |
| GET | `/logistics/providers/{id}/ratings` | User | A provider's ratings, newest first (`?page=&pageSize=`). Hidden with the profile: a withdrawn provider's ratings are a 404 for strangers, readable by its owner |
| POST | `/orders/{orderId}/logistics-bookings` | Participant | `{ providerId, mode, services?, insuredValue?, notes? }` — either party to the order may ask. `services` are drawn from the provider's offer; `insuredValue` accompanies `Insurance`. An order the caller has no part in is a 404, not a 403 |
| GET | `/orders/{orderId}/logistics-bookings` | Participant | The order's bookings; both parties see the same list |
| GET | `/logistics/bookings` | User | Everything the caller raised or was named in; `?status=` |
| GET | `/logistics/bookings/{id}` | Participant | One booking |
| POST | `/logistics/bookings/{id}/respond` | LSP | `{ accept, notes? }` — the named provider accepts or declines an open request |
| POST | `/logistics/bookings/{id}/cancel` | Requester | Withdraw an un-answered request |
| POST | `/logistics/bookings/{id}/complete` | Requester | Close an accepted booking as fulfilled |
| POST | `/logistics/bookings/{id}/rating` | Requester | `{ rating (1–5), comment? }` — rates the provider once the booking is `Completed`. Moves the provider's headline rating |

Booking rules and errors: only a **listed and active** provider can be booked
(`PROVIDER_NOT_FOUND`, 404); a draft or cancelled order is closed to new bookings (`ORDER_CLOSED`,
409); one open or agreed booking per provider per order, so a second request is a duplicate
(`BOOKING_EXISTS`, 409). Only the named provider may answer (`NOT_PROVIDER`, 403), only the requester
may withdraw or complete (`NOT_REQUESTER`, 403), and only an un-decided request can be withdrawn or
answered and only an accepted one completed (`BOOKING_CLOSED` / `BOOKING_NOT_ACCEPTED`, 409).
Ancillary services must be a subset of the provider's offer (`SERVICE_NOT_OFFERED`, 400); engaging
`Insurance` requires `insuredValue` (`INSURED_VALUE_REQUIRED`, 400) and a value without insurance is
refused (`INSURANCE_NOT_REQUESTED`, 400). Validation failures (bad mode/service, missing coverage,
an email that is not an email, a non-positive insured value) are 400s.

Rating rules and errors: only the **requester** may rate (`NOT_REQUESTER`, 403) and only a
`Completed` booking (`BOOKING_NOT_COMPLETED`, 409); one rating per booking, so a second attempt is a
`RATING_EXISTS` conflict. A rating outside 1–5 (including the `0` a missing score deserialises to) is
a 400. All ratings — and the caller's own rating on a booking (`myRating`) — are read back through
the profile and booking reads.

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
| POST | `/admin/payments/{id}/refund` | Admin | Refund a settled payment: books a compensating negative `Refund` ledger entry; `amount` defaults to the payment's remaining refundable and can never exceed it, `reason` (optional) is recorded on the entry and the audit trail. A full refund moves the payment to `Refunded` and sets `RefundedAtUtc`; the order re-rolls up, a Paid invoice reopens, and both parties are notified. Refused (409) on pending/failed/refunded payments; over-refunds rejected (400) |
| GET | `/admin/orders/{id}/payments` | Admin | Payments on one order for the refund screen, each carrying its refunded total so an administrator can see what is still refundable |

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

The planned `/market/*` surface became a seeded `/marketdata/*` API — the public page
(`/market-intelligence`, and "Market data" in the header) reads the same rows it renders, so a
trend line, a card's latest price and the observations table always describe the same selection.

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| GET | `/marketdata/prices` | Public | Paginated price series: `?commodity=&country=&currency=&from=&to=&sort=` (`priceAsc\|priceDesc\|reportedAsc\|reportedAtDesc`) |
| GET | `/marketdata/destination-requirements` | Public | The documents a route is likely to need, from `?origin=&destination=` ISO-3166 alpha-2 codes. Returns each document with `mandatory` + a one-line `reason`, a `domestic` flag when the codes match, and a corridor `notes` line. A code that is not a country is a 400 `INVALID_COUNTRY` |
| GET | `/admin/marketdata` | Admin | Same rows, for maintenance |
| POST | `/admin/marketdata` | Admin | Publish a price observation |

The page groups the feed into trend lines per commodity × origin × unit × currency, draws them as
hand-rolled SVG charts (no chart dependency), and shows a Δ-over-previous trend indicator per
line. The destination-requirements lookup sits above them (a curated guide, not a legal ruling); the
market report pages remain planned content work, not API surface.

## Admin

Implemented in tasks 2.3, 2.7 and the Phase 4 admin-operations slice. Every route below is live and
every queue has an admin screen; the audit trail and the ledger can also be taken away as CSV. There
is no remaining planned administrative surface.

| Method | Route | Description |
| --- | --- | --- |
| GET | `/admin/overview` | **One request, the whole dashboard.** Platform counts, four funnels (business verification, document verification, order status, order payment status), corridor value, per-category supply vs demand, RFQ demand. |
| GET | `/admin/audit-log` | Administrator decisions, newest first. `?action=&userId=&q=` |
| GET | `/admin/audit-log/export` | The same trail as a **CSV download** under the same `?action=&userId=&q=` filters and ordering, with no page cap. Streamed, and the file opens with a UTF-8 BOM so Excel reads non-ASCII text (a trading name, a rejection reason) correctly. |
| GET | `/admin/users` | User directory. `?q=&role=&emailConfirmed=&lockedOut=&hasBuyerId=`. `q` matches name, email, business trading name, or buyer account number; `role` is a role **name**, not an id — ids are database-generated and undiscoverable through the API, so an id filter would be a parameter no client could fill. `hasBuyerId=true` keeps only accounts holding a buyer account number (and `false` the complement) — the dashboard's buyer panel counts buyers this way, because a buyer-side account may hold any of Buyer/Importer/Distributor and a single role name would count a subset. |
| GET | `/admin/orders` | Global order queue. `?status=&paymentStatus=&q=` |
| GET | `/admin/shipments` | Global consignment queue. `?status=&q=&hasGuestTracking=` |
| GET | `/admin/businesses` | Business queue. `?status=&q=` |
| POST | `/admin/businesses/{id}/verification/review` | Approve/reject with notes; sets the verified badge. Writes an audit entry. |
| GET | `/admin/documents` | Document review queue. `?status=&docType=&q=` |
| POST | `/admin/documents/{id}/review` | Approve/reject with notes. Writes an audit entry. |
| GET | `/admin/products` | Product moderation queue — listings across every seller. `?listingStatus=&moderationStatus=&q=&categoryId=`. The two statuses are independent — `listingStatus` is the seller's choice, `moderationStatus` the platform's — and `isVisibleInMarketplace` is derived from both, so a client renders it rather than re-deriving it |
| PATCH | `/admin/products/{id}/moderation` | Suspend (`action=0`) or reinstate (`action=1`) a listing. Suspension requires a `reason`; re-suspending a suspended listing is a 409, and reinstating deliberately does not publish a draft. Writes an audit entry |
| GET | `/admin/rfqs` | All RFQs + volume filters. `?status=&q=&unanswered=` — `unanswered` is a property of the RFQ (no quotation at all), not of any one supplier |
| GET | `/admin/transactions` | The transaction ledger across every order. `?q=&type=&currency=&orderId=&from=&to=` |
| GET | `/admin/transactions/export` | The same ledger as a **CSV download** under the same `?q=&type=&currency=&orderId=&from=&to=` filters and ordering, with no page cap. The amount column is signed and has no thousands separator, so the file can be summed straight from the spreadsheet. |
| GET | `/admin/reports/{kind}` | One analytics report. `kind` is `corridors`, `rfq`, `verification-funnel` or `commodities`; `?from=&to=` is an inclusive window. An unknown kind or an inverted window is a 400. |
| GET | `/admin/settings` | The operator-editable settings, each with its catalogue default. |
| PUT | `/admin/settings` | Partial update — only the keys sent are written. Every change is validated before any is written; one audit entry per call. |
| PATCH | `/admin/users/{id}/status` | Suspend (`action=0`) or reactivate (`action=1`) an account. Suspension requires a `reason`; suspending yourself is refused. Writes an audit entry. |

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

### Ledger, reports, settings and account status

`GET /admin/transactions` reads the **ledger rows themselves**, not a payment rollup. Refunds and
fees are booked as signed entries rather than by editing the payment they relate to, so a corrected
payment adds a row instead of rewriting history, and an administrator can reconcile the page against
a bank statement line by line. `q` searches the ledger reference, the external reference (a bank
trace id), the narrative and the order number; `type`, `currency` and the inclusive `from`/`to`
window on `OccurredAtUtc` are exact filters.

The audit trail and the ledger each have a **CSV export** beside their list. The export reuses the
list's filter, so the two can never disagree about what a filter means, but drops the page cap: an
operator reconciling a statement needs the rows themselves rather than a screenful at a time. The
writer quotes only the fields that need it, so a comma in a rejection reason cannot shift a column.
Both responses stream and are only reachable by an administrator; the frontend fetches them as blobs
(a link would send no bearer token) and saves them in place.

`GET /admin/reports/{kind}` returns one `AdminReportDto` **envelope with exactly one payload
populated** according to `kind`; the other payload fields stay empty, so a client switches on `kind`
and reads the one shape it asked for. Four endpoints would repeat the same window parsing and
authorisation four times, and a generic row shape would throw away the per-report types the client
already knows. `from`/`to` are inclusive and optional; an inverted window and an unknown kind are
both 400s. The windows are honest about what they bound: corridors filter **both** sides of the
reconciliation — orders on `PlacedAtUtc`, ledger entries on `OccurredAtUtc` — so a period cannot
show orders whose payments sit outside it. The RFQ report, the commodities report and the
verification funnel each filter their subject on `CreatedAtUtc`; the commodities report's supply side
is deliberately the *current* catalogue rather than a windowed timestamp.

`GET/PUT /admin/settings` exposes a **code-defined catalogue**, not an arbitrary key/value store.
Only keys that running code actually reads appear — a setting nothing consumes would be a screen
that lies about its reach. `PUT` is partial (omitted keys keep their value or default), validates
every change before writing any, and records one `settings.updated` audit entry for the batch. The
catalogue today:

| Key | Kind | Default | What it changes |
| --- | --- | --- | --- |
| `registration.open` | boolean | `true` | `false` makes `POST /auth/register` return 403 `REGISTRATION_CLOSED`. |
| `registration.default_role` | role | `Buyer` | The role assigned to a registrant who states none, in `POST /auth/register`. |

`PATCH /admin/users/{id}/status` sets the platform's own `IsActive` flag, which is distinct from
Identity's `LockedOut` (a failed-sign-in throttle that clears itself). Suspension is checked on
sign-in **and** on token validation, so it ends an existing session at that session's next request
rather than only blocking the next login; reactivation is the resolution and takes no reason.
Suspension requires one, and a no-op transition or suspending yourself is a 409. Accounts are never
hard-deleted — `AuditLog.UserId` is `ON DELETE RESTRICT` precisely so removing an account cannot
remove the evidence that it approved a transaction.

## Not in scope until noted

Escrow, AI, price-feed ingestion API (internal job), and customs-service integrations
(Phase 5). Domestic logistics-provider *directory/booking* shipped in Phase 4 — Slice 3, above.

## Realtime model

- **Notifications hub** (`/hubs/notifications`, bearer): push on RFQ/quote/order/
  document/payment/shipment/verification/logistics events.
- **Messages hub** (`/hubs/messages`): join per `conversationKey`; `Send`/`Read` events.
- Fallback: poll `GET /me/notifications?unreadOnly=true` every 30s.