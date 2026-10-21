# 06 — Decisions & Risks

## Positioning decisions (recorded)

| # | Decision | Choice | Impact |
| - | --- | --- | --- |
| P1 | **Core product** | The platform **is an import/export platform first** — cross-border is the MVP; domestic retail follows (Phase 4) | Phases, MVP scope, API, data model all center on corridors, trade terms, trade documents, shipments |
| P2 | **Trade corridor** | **Pan-African / global, corridor-neutral** — no single-country bias; every business sets its own origin(s) and destination(s) | Country metadata everywhere; `BusinessTradeCountries`; no Nigeria-only logic; multi-currency by design |

## Open decisions (need a human decision before the workstream starts)

| # | Decision | Options | Suggested default | Needed by |
| - | --- | --- | --- | --- |
| D1 | **Payment gateway per corridor** | Paystack (NGN), Stripe (USD/EUR/GBP), Flutterwave (NGN+intl), Adyen (enterprise/CNY) behind one `IPaymentGateway` adapter | **Stripe (USD/EUR) as gateway #1** since MVP trade executes in USD; Paystack second; record off-platform transfers for corridors without gateways | Phase 2 (gateway #1) / Phase 3 (matrix) |
| D2 | **File storage** | Local disk (dev) → Cloudinary / AWS S3 | `IFileStorage` with LocalDisk now; **Cloudinary** first (image transforms for product photos + doc previews) | Phase 1 uploads |
| D3 | **Email / SMS provider** | SendGrid / Mailjet / resend / MailKit+SMTP; SMS later (Twilio, Termii) | **Resend** (transactional API, behind `IEmailSender`) for production; MailKit+SMTP as the fallback; console sink for local dev. SMS still later | Phase 0 (stub), Phase 3 (real) |
| D4 | **Cloud deployment target** | Azure App Service + Azure SQL; AWS; containers | **Azure App Service + Azure SQL** (matched to the SQL Server stack) | Phase 5; optional containers earlier |
| D5 | **Verification depth in MVP** | Manual admin review vs 3rd-party KYC/KYB (Smile ID, IdentityPass, Youverify) | Manual admin review in MVP; evaluate KYB provider at Phase 3 scale (cross-border fraud risk) | Phase 1 |
| D6 | **Escrow / trade-finance partner** | Regulated financial institutions for escrow-style payments | **Do not build** until a partner exists; keep transaction ledger escrow-compatible (spec §15) | Phase 5 (legal) |
| D7 | **Market-data source** | Admin entry → commodity exchanges, ministry datasets, price feeds | Admin-entered seeded series MVP; integrate a real source at Phase 3 | Phase 3 |
| D8 | **Brand / domain / legal entity** | Founder decision | TBD; no code impact | Before launch |
| D9 | **MVP currency execution** | USD/EUR only vs NGN+USD vs all five | **Multi-currency representation everywhere** (schema, quoting, orders); **execution via gateway #1 + recorded transfers**; expand gateway matrix at Phase 3 | Phase 2 |
| D10 | **HS-code depth** | Free-text field vs validated HS taxonomy service | Free-text with starter map now; validated/customs-integrated taxonomy at Phase 5 | Phase 1 |
| D11 | **Corridor legal scope first** | West/East Africa + EU/US buyers vs wider | Let demand decide; design is corridor-neutral so no rework | Phase 4/5 |

**Decision-record discipline:** append decisions to these tables (decided, date, choice,
impact) as they are made.

## Task decisions (recorded)

Positioning is P1/P2 above; these are implementation decisions that a later reader could
reasonably have made the other way, and why.

| # | Decision | Choice | Impact |
| - | --- | --- | --- |
| T1 | **What the audit log records** | Administrator **decisions**, logged explicitly at each call site — not a generic entity-change interceptor | Only two call sites today (business verification, document review). A change-data-capture feed would bury those among audit-field stamps derivable from the row itself. `Action`/`EntityType` are **text**, so a new admin action needs no migration |
| T2 | **How the log is written** | `AuditLogWriter.Record` is synchronous and **save-less** — it adds to the change tracker and returns | The decision and the record of it commit in one transaction. A separate write could leave an approval with no evidence, which is the one failure an audit log must not have |
| T3 | **What happens to audit rows on user deletion** | `AuditLog.UserId` is `ON DELETE RESTRICT`; reads substitute the raw id for a missing account | Deleting a user cannot delete the evidence they approved a transaction. Users are suspended via `IsActive`, not hard-deleted |
| T4 | **How corridor value is grouped** | By `(PortOfLoading, DestinationCountry, Currency)` — currency is part of the row's identity | No FX rate exists anywhere in the system, so a total that mixed currencies would be a sum of incomparable numbers. Corridors are uncapped; the commodity panel is capped at 12 as an explicit "top N" |
| T5 | **One KPI for corridor money, or two?** | **Both** — gross order value and net settled payments, side by side | They answer different questions. Collapsing them hides unpaid orders: a corridor can look healthy on gross volume while nothing has been paid |
| T6 | **Source of "settled"** | The **transaction ledger**, not the order's payment rollup | A ledger entry denominated differently from its order forms its own row and is reported as `mismatchedLedgerEntries`, rather than silently distorting a corridor total |
| T7 | **What the funnels count** | **Orders** by payment status (not payments by payment status), rendering every enum stage including empty ones | An omitted stage is indistinguishable from a bug; a rendered zero is information |
| T8 | **Supply vs demand** | Merged per category, driven by the **union** of listed and RFQ'd categories; `rfqCount` (lifetime) and `openRfqCount` (live) reported separately | A category buyers ask for and nobody lists is the catalogue gap the panel exists to reveal, and iterating the product list omitted it silently. The gap flag keys off *open* RFQs — a closed RFQ from years ago is history, not demand |
| T9 | **Dashboard shape** | `GET /admin/overview` is **one endpoint**, not eight | A dashboard is one screen. Eight calls means eight loading states, and the tiles would render against each other only by accident |
| T10 | **Status labels** | Supplied by `AdminService`, not embedded in the DTOs | A status enum must not leak into a client contract. The single exception (`AdminFunnelStageDto.label`) is deliberate — a funnel stage with no name is not renderable |
| T11 | **How timestamps are stored and shown** | Persisted as UTC, serialised with a trailing `Z`, and rendered by the shared `formatUtc` / `formatUtcDate` helpers, labelled "UTC" | A `datetime2` carries no offset, so the model marks every value read from the database `Utc` and the wire value becomes an unambiguous instant. Formatting in the reader's own zone would still move a recorded moment — on a UTC+1 machine a review recorded at 09:00 would read as 10:00 — so display is fixed to UTC. A field that is a date in substance (an expiry, a deadline, a price date) uses the date-only helper so the day cannot shift across the date line |

---

## Risk register

| # | Risk | Likelihood | Impact | Mitigation |
| - | --- | --- | :--: | :--: | --- |
| R1 | **Cross-border counterparty fraud** (fake exporters/importers) | High | High | Document-based verification (D5), Verified badge everywhere, trade-countries transparency, reviews (Phase 3), dispute workflow, KYB at scale |
| R2 | **Document rejection at customs** (COO, phytosanitary, permits) | Medium | High | First-class trade-document workflows + admin verification, destination-requirement helper (Phase 3 market data), doc-type library aligned to real corridors |
| R3 | **Payment double-charge / webhook replay** | Medium | High | `Idempotency-Key` on order + payment; signature-verified webhooks; unique provider-ref index; `RowVersion` on Payment so a concurrent event's save loses and re-reads committed state rather than overwriting a settlement; integration tests |
| R4 | **Corridors without gateway coverage** | High | Medium | **Off-platform payment recording** in MVP (bank transfer / L/C references) so trade is never blocked by gateways; gateway matrix escalates per demand |
| R5 | **Currency volatility + conversion misunderstandings** | Medium | High | Price/quote/order all store currency explicitly; quotes carry validity windows; totals negotiated in agreed currency; UX shows currency on every amount |
| R6 | **Incoterms misuse (buyer/seller confusion over risk/cost split)** | Medium | High | **Incoterms shown and required** on listings, RFQs, quotes, orders; education copy in UI; server stores the negotiated term |
| R7 | **RFQ lifecycle violations** | Medium | Medium | Server-enforced transitions on Quote→Order handoff; expired-RFQ job; state-machine unit tests |
| R8 | **Inventory oversell** | Medium | Medium | Reserved stock on order placement; `RowVersion` optimistic concurrency; release on cancel/expiry |
| R9 | **Scope creep during MVP** | High | Medium | `05-mvp-scope.md` is the contract; parking lot explicit; new ideas go to Phase 3+ |
| R10 | **Data protection / privacy across jurisdictions** | Medium | High | NDPR/GDPR-informed posture: RBAC, document access controls, PII-minimized logs, audit log, retention policy; legal review before Phase 4 (domestic) and Phase 5 (global) |
| R11 | **SQL Server cost/licensing in cloud** | Low | Medium | Azure SQL managed; reserve sizing at Phase 5 |
| R12 | **Single-developer velocity** | High | Medium | Tight MVP scope; honest story sizes; CI from day one; vertical-slice reviews |
| R13 | **Logistics data trust (tracking events)** | Medium (Phase 3) | Low-Med | Carrier records events (append-only); participants see same timeline; disputes cover tracking discrepancies |
| R14 | **Gateway/provider lock-in** | Low | Medium | `IPaymentGateway` / `IFileStorage` / `IEmailSender` adapters; provider refs stored per payment/event |
| R15 | **API timestamps carry no time zone** — **resolved** | Low | Low | `datetime2` has no offset and EF materialised it as `Unspecified`, so `System.Text.Json` emitted no `Z` and `new Date()` read the wall-clock as **local**, shifting every displayed time by the reader's offset (and, for a date-in-substance field, potentially a whole day). Fixed at the source: a model convention in `AgroTradeDbContext` marks every timestamp read from the database `Utc`, so the API emits an unambiguous instant ending in `Z`. The web client also parses through `parseUtc` — which restores a missing `Z` — and routes every render through `formatUtc` / `formatUtcDate`, so a display cannot drift even if the wire format regresses. Pinned by `UtcTimestampConventionTests` |
| R16 | **Concurrent write races on tokens and payments** — **resolved** | Low | Medium | A refresh-token rotation is a read-then-write, so two simultaneous refreshes could both pass the "not revoked" check and both mint a live successor; two gateway events for one charge could let the later save overwrite a settlement. `RefreshToken` and `Payment` now carry a database-generated `RowVersion`: rotation commits the revoke and the successor in one `SaveChanges`, so the loser rolls back and is treated as reuse (family revoked), and the webhook apply re-reads committed state and re-evaluates on a lost save. Request-path refunds translate the conflict to `409 PAYMENT_CHANGED`, and a refund's ledger entry and payment state now commit in the same transaction (a partial refund touches the payment's audited timestamp to join it), so two simultaneous refunds cannot both read the same balance and over-refund. Pinned by `ConcurrencyTokenTests` plus concurrent-write integration tests. Deliberate non-goal: background workers assume a single node |

## Security & compliance controls mapped (spec §18)

| Spec control | Mechanism |
| --- | --- |
| KYC/KYB verification | Business documents + admin review (D5); verification submission workflow |
| Fraud / suspicious-transaction monitoring | Transaction ledger, audit log, rate limits, dispute queue; Phase 5 anomaly alerts |
| Secure payment processing | Signature-verified webhooks, idempotency, regulated providers only (D1) |
| Data protection & privacy | RBAC, document access controls, PII-minimized logs, retention policy, NDPR/GDPR posture |
| Audit logs | `AuditLog` table written **explicitly** at each admin decision (business verification, document review) via `AuditLogWriter.Record`, in the same transaction as the action itself ⇄ `GET /admin/audit-log`. **Correction (task 2.7):** this line previously claimed a `SaveChanges` interceptor. There is no such interceptor, and there should not be — it only ever stamped `CreatedAtUtc`/`UpdatedAtUtc` on entities and was never a change-logger. An interceptor that logged every changed entity would bury the entries that matter among audit-field stamps already derivable from the row. |
| Role-based access control | Identity roles + policies + ownership rules in services |
| Document access controls | Per-document authorization by context participant (order/RFQ/business) |
| Country/commodity import-export requirements | HS codes + required certs on RFQs/listings; destination-requirements helper (Phase 3); doc-type library aligned to real corridors |
| Clear terms & dispute policies | Dispute module + ToS owned by D8 before launch |

## Suggested first actions

1. Accept the cross-border-first positioning (P1/P2 — done above), decide **D8** branding.
2. Start Phase 0 backlog (stories 0.1 → 0.7).
3. `git init` + private remote + CI on first PR.
4. Lock **D1** (gateway #1 = suggested Stripe USD) and **D9** before Phase 2 payment work.