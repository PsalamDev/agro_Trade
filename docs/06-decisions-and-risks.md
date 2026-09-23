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
| D3 | **Email / SMS provider** | SendGrid / Mailjet / resend / MailKit+SMTP; SMS later (Twilio, Termii) | MailKit + SMTP in dev; transactional API at first cloud deploy | Phase 0 (stub), Phase 3 (real) |
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

---

## Risk register

| # | Risk | Likelihood | Impact | Mitigation |
| - | --- | --- | :--: | :--: | --- |
| R1 | **Cross-border counterparty fraud** (fake exporters/importers) | High | High | Document-based verification (D5), Verified badge everywhere, trade-countries transparency, reviews (Phase 3), dispute workflow, KYB at scale |
| R2 | **Document rejection at customs** (COO, phytosanitary, permits) | Medium | High | First-class trade-document workflows + admin verification, destination-requirement helper (Phase 3 market data), doc-type library aligned to real corridors |
| R3 | **Payment double-charge / webhook replay** | Medium | High | `Idempotency-Key` on order + payment; signature-verified webhooks; unique provider-ref index; integration tests |
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

## Security & compliance controls mapped (spec §18)

| Spec control | Mechanism |
| --- | --- |
| KYC/KYB verification | Business documents + admin review (D5); verification submission workflow |
| Fraud / suspicious-transaction monitoring | Transaction ledger, audit log, rate limits, dispute queue; Phase 5 anomaly alerts |
| Secure payment processing | Signature-verified webhooks, idempotency, regulated providers only (D1) |
| Data protection & privacy | RBAC, document access controls, PII-minimized logs, retention policy, NDPR/GDPR posture |
| Audit logs | `SaveChanges` interceptor + explicit admin action logging ⇄ `GET /admin/audit-log` |
| Role-based access control | Identity roles + policies + ownership rules in services |
| Document access controls | Per-document authorization by context participant (order/RFQ/business) |
| Country/commodity import-export requirements | HS codes + required certs on RFQs/listings; destination-requirements helper (Phase 3); doc-type library aligned to real corridors |
| Clear terms & dispute policies | Dispute module + ToS owned by D8 before launch |

## Suggested first actions

1. Accept the cross-border-first positioning (P1/P2 — done above), decide **D8** branding.
2. Start Phase 0 backlog (stories 0.1 → 0.7).
3. `git init` + private remote + CI on first PR.
4. Lock **D1** (gateway #1 = suggested Stripe USD) and **D9** before Phase 2 payment work.