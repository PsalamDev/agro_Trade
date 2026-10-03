# 02 — Database Design

EF Core 10 against SQL Server. Schema is designed **around cross-border trade**: every
money value carries a currency, every trade carries origin/destination countries and
ports, and trade documents are first-class entities.

## Conventions

| Concern | Decision |
| --- | --- |
| Primary keys | `Guid` on all aggregates, generated in app code |
| Money | `decimal(18,2)` + separate `Currency` (ISO-4217 `varchar(3)`) — never floats |
| Countries | ISO-3166 alpha-2, `char(2)` (e.g. `NG`, `KE`, `CN`) |
| Time | All timestamps `datetime2` in **UTC**; column suffix `Utc` |
| Soft delete | `IsDeleted` on large/user-authored aggregates (Product, Business) |
| Identity | ASP.NET Core Identity: `ApplicationUser : IdentityUser<Guid>` |
| Naming | FKs named `XId` from navigation property |
| Audit | `CreatedAtUtc`, `UpdatedAtUtc` (+ `CreatedById`/`UpdatedById` where relevant) |
| Indexes | One per FK; composite search indexes per table |
| JSON | Small flexible payloads (certifications, specs, settings) as `nvarchar(max)` JSON |
| Concurrency | `RowVersion` on Order, Product, Inventory, Quotation |

## Entity groups (29 tables)

```
┌─ IDENTITY & ACCESS ──────────────────────────────┐
│  Users · Roles · UserRoles · Profiles · Addresses │
├─ BUSINESS & TRUST ───────────────────────────────┤
│  Businesses · BusinessTradeCountries ·           │
│  BusinessDocuments · BusinessVerification ·      │
│  BusinessLocations · Reviews · Disputes          │
├─ CATALOG ────────────────────────────────────────┤
│  Categories · Products · ProductImages · ProductLocations · Inventory │
├─ COMMERCE ───────────────────────────────────────┤
│  Orders · OrderItems · Payments · Transactions   │
├─ TRADE REQUESTS ─────────────────────────────────┤
│  TradeRequests (RFQs) · Quotations · QuotationItems │
├─ COMMUNICATION ──────────────────────────────────┤
│  Messages · Notifications                        │
├─ LOGISTICS ──────────────────────────────────────┤
│  Shipments · ShipmentTrackingEvents              │
├─ DOCUMENTS ──────────────────────────────────────┤
│  Documents                                       │
└─ INTELLIGENCE ───────────────────────────────────┘
   MarketData
```

---

## Identity & access

### Users
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK (IdentityUser<Guid>) |
| DisplayName | nvarchar(150) | |
| Email / UserName | | from Identity, unique |
| PhoneNumber | | from Identity (WhatsApp-enabled for trade comms) |
| IsActive | bit | admin suspend flag |
| IsEmailVerified | bit | email confirm flow |
| DefaultCurrency | varchar(3) | ISO code |
| HomeCountry | char(2) | ISO-3166 |
| CreatedAtUtc / UpdatedAtUtc | | |

### Roles · UserRoles
Standard Identity tables. Seed roles (spec §6): `Buyer`, `Farmer`, `Supplier`,
`AgroProcessor`, `Exporter`, `Importer`, `Distributor`, `LogisticsProvider`,
`Administrator`. Users hold **multiple roles** (e.g. Farmer + Exporter).

### Profiles
1:1 with User. `AvatarUrl`, `Bio`, `Language`, `Website`, `ExportExperience?`,
`ProfileVisibility`.

### Addresses
Polymorphic owner: `OwnerUserId?`, `OwnerBusinessId?`, `IsPrimary bit`, `Line1/2`,
`City`, `State`, `Country char(2)`, `PostalCode`, `Latitude?`, `Longitude?`.
Index `(OwnerBusinessId, IsPrimary)`.

---

## Business & trust

### Businesses
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| OwnerUserId | guid | FK Users |
| LegalName / TradingName | nvarchar | TradingName shown publicly |
| RegistrationNumber | nvarchar(100)? | |
| BusinessType | int | SoleProprietor, Partnership, LimitedCompany, Cooperative, NGO, Other |
| CategoryFocus | guid? | FK Categories |
| Website / Description | | |
| VerificationStatus | int | **VerificationStatus** enum |
| IsVerifiedBadgeVisible | bit | derived from VerificationStatus |
| Rating / RatingCount | decimal(3,2) / int | denormalized from Reviews |
| IsActive / IsDeleted | bit | |

### BusinessTradeCountries
**What a business trades and where** — the backbone of cross-border reachability:
`Id, BusinessId FK, Direction int (Export/Import), Country char(2), IsPrimary bit`.
Unique `(BusinessId, Direction, Country)`. Index `(Direction, Country)` powers
"exporters that export to Nigeria" searches.

### BusinessDocuments
`Id, BusinessId FK, DocType int (DocumentType), FileUrl, FileName, UploadedAtUtc,
VerifiedByUserId?, VerifiedAtUtc?`. One active copy per `(BusinessId, DocType)`.

### BusinessVerification
`Id, BusinessId FK, Status (VerificationStatus), RequestedAtUtc, ReviewedAtUtc,
ReviewedByUserId?, Notes?, VerifiedLevel (Basic/Enhanced)`.

### BusinessLocations
`Id, BusinessId FK, Name, City, State, Country char(2), IsPrimary`. Fulfilment/pickup
points for orders and logistics.

### Reviews
`Id, ReviewerUserId FK, TargetBusinessId FK, OrderId FK (unique per direction per
order), Rating tinyint (1–5, check), Comment?, IsPublished bit, CreatedAtUtc`.
Index `(TargetBusinessId, IsPublished)` includes Rating.

### Disputes
`Id, OrderId FK, RaisedByUserId FK, AgainstBusinessId FK, Reason int, Details,
Status int (DisputeStatus), ResolvedById?, Resolution?, CreatedAtUtc, ResolvedAtUtc?`.

---

## Catalog

### Categories
Self-referencing tree: `Id, ParentId?, Name, Slug (unique), Icon, SortOrder, IsActive`.
Seed the 12 top-level categories from spec §4 (Grains & cereals … Agro-processing
equipment) plus commodity children as needed (e.g. Cashew → Rain-fed / Grades).

### Products (export listings)
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| SellerBusinessId | guid | FK Businesses |
| CategoryId | guid | FK Categories |
| Name / Slug | nvarchar / unique | |
| Description | nvarchar(max) | |
| **HsCode** | nvarchar(10)? | **Harmonized System** customs classification (e.g. `0801` cashew) |
| OriginCountry | char(2) | **required** |
| **PortOfLoading** | nvarchar(100)? | e.g. "Mombasa (MBA)" |
| Price | decimal(18,2) | per PriceUnit, in `Currency` |
| PriceUnit | int | PerKilogram, PerTon, PerBag, PerCarton, PerCrate, PerUnit, PerLitre, PerHead, PerContainer, Other |
| Currency | varchar(3) | |
| AvailableQuantity | decimal(18,2) | |
| MinimumOrderQuantity | decimal(18,2) | MOQ (bulk trade) |
| **Incoterms** | int? | **enum: EXW, FCA, FAS, FOB, CFR, CIF, CPT, CIP, DAP, DPU, DDP** |
| **LeadTimeDays** | int? | dispatch lead time |
| PaymentTermsOffered | nvarchar(max)? | JSON, e.g. ["T/T 50/50", "L/C at sight"] |
| QualitySpecs | nvarchar(max) | JSON: grade, moisture, purity, size |
| PackagingOptions | nvarchar(max) | JSON, e.g. ["50 kg PP bag", "1 MT jumbo bag"] |
| Certifications | nvarchar(max) | JSON, e.g. ["GlobalG.A.P.", "ISO 22000", "Phytosanitary"] |
| ListingStatus | int | Draft / Active / Inactive |
| VerificationStatus | int | Unverified / Pending / Verified / Rejected |
| IsDeleted | bit | soft delete |
| CreatedAtUtc / UpdatedAtUtc | | |

Indexes: `(IsDeleted, ListingStatus, VerificationStatus)`, `(CategoryId)`,
`(SellerBusinessId)`, `(OriginCountry)`, `(HsCode)`, `(Name)` (full-text at Phase 5),
`(CreatedAtUtc DESC)`.

> **Export vs domestic:** every listing is origin-country-coded and export-capable.
> Destination reachability comes from `BusinessTradeCountries`. Domestic-only listings
> (Phase 4) reuse the same table with domestic terms.

### ProductImages
`Id, ProductId FK, Url, AltText, SortOrder, IsPrimary bit`.

### ProductLocations
`Id, ProductId FK, Name, City, State, Country char(2), IsPrimary`.

### Inventory
`Id, ProductId FK (unique), QuantityOnHand, QuantityReserved, RowVersion`.
Available = OnHand − Reserved. Reserve on order, decrement on confirm, release on cancel
(transaction-scoped, optimistic).

---

## Commerce

### Orders (cross-border)
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| OrderNumber | nvarchar(20) | `AG-YYYY000001`, unique sequence |
| BuyerUserId | guid | FK Users |
| SellerBusinessId | guid | FK Businesses |
| Status | int | **OrderStatus** enum |
| Currency | varchar(3) | agreed trade currency |
| Subtotal / Discount / Total | decimal(18,2) | |
| **Incoterms** | int | negotiated |
| **PortOfLoading / PortOfDischarge** | nvarchar(100) | |
| **PaymentTerms** | int | **PaymentTerms** enum (T/T advance, T/T partial, L/C, CAD, on account, other) |
| PaymentStatus | int | **PaymentStatus** |
| DestinationAddressId | guid? | FK Addresses |
| TransportMode | int? | ShipmentMode enum |
| Notes | nvarchar(max)? | |
| PlacedAtUtc / UpdatedAtUtc | | |
| RowVersion | timestamp | |

### OrderItems
Snapshot: `Id, OrderId FK, ProductId? FK, ProductNameSnapshot, Quantity, Unit, UnitPrice,
LineTotal, Currency`.

### Payments
`Id, OrderId FK, Reference (unique), Amount, Currency, Provider int (PaymentProvider),
ProviderRef, Status (PaymentStatus), Method int (PaymentMethod), RemittanceReference?,
BankName?, ValueDateUtc?, InitiatedAtUtc, PaidAtUtc?, FailureReason?, Notes?,
IdempotencyKey (unique, filtered index)`. **Off-platform payments** (bank transfer/T&T, L/C) are
recorded by the **seller** with `Provider=ManualTransfer` and a bank remittance reference -- no
webhook. They are stored as already `Paid`: there is no provider to poll, so a pending state would
only mean "waiting for a human to come back and finish typing this". Partial payments are the
norm rather than an edge case (a T/T deposit against the balance), so an order may hold several.

`RemittanceReference` is required for off-platform payments and **unique per order**, via a
filtered index on `(OrderId, RemittanceReference)`. It is the only externally checkable detail on
a record nobody witnessed, and it is what makes a double-entry detectable: re-recording one bank
transfer is the likeliest mistake here, and without the guard it silently doubles the order's paid
figure. Scoped per order rather than globally, because trace ids are only unique within a bank.

`Status` is terminal at `Paid`. Settled money on a card rail does not un-settle, so a failing
gateway event arriving after a settlement is recorded with outcome `SupersedesSettlement` and not
applied -- see `PaymentWebhookEvent`. A gateway that really did claw money back is describing a
**refund**, which needs a compensating ledger entry rather than a status flip.

### Transactions
Ledger: `Id, OrderId FK, PaymentId? (correlation only, no FK), Type (Payment/Refund/Fee),
Amount, Currency, OccurredAtUtc, Reference, Narrative, ExternalReference, CreatedAtUtc, UpdatedAtUtc`.
One immutable row per movement of money: written when the money moves, never edited.

A row is a statement of fact, not a status. A payment is a mutable attempt; a ledger entry is
something that already happened. There is deliberately no `Direction` or `Status` column -- money in is a
positive `Amount`, and whether it arrived is the payment's business, not the ledger's.
`ExternalReference` carries whichever identity the counterparty gave the money -- a gateway charge id or
a bank's remittance trace id -- as that is the column an auditor reconciles against.

`PaymentId` is a plain correlation column rather than a foreign key, so an entry outlives the payment it
came from. A filtered unique index on `PaymentId` where it is not null and `Type = Payment` stops a payment
being counted twice.

---

## Trade requests

### TradeRequests (RFQs — importer sourcing)
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| RfqNumber | nvarchar(20) | `RFQ-YYYY000001` unique |
| BuyerUserId | guid | FK Users |
| Title / Description | | |
| CategoryId | guid? | |
| Quantity / Unit | decimal + int | |
| **DestinationCountry** | char(2) | **required** |
| **DestinationPortOrCity** | nvarchar(100)? | |
| **PreferredIncoterm** | int? | responsive to EXW/FOB/CIF/DDP |
| **TransportMode** | int? | ShipmentMode |
| **InsuranceRequired** | bit | |
| TargetSpecs | nvarchar(max) | JSON quality spec |
| CertificationsRequired | nvarchar(max) | JSON: phytosanitary, COO, inspection… |
| **PreferredCurrency** | varchar(3) | |
| **PaymentTermsPreferred** | int? | PaymentTerms |
| DeliveryDeadline | datetime2? | |
| BudgetMin / BudgetMax | decimal(18,2) | in PreferredCurrency |
| Status | int | **RfqStatus** |
| ExpiresAt | datetime2 | auto-close job |
| CreatedAtUtc / UpdatedAtUtc | | |

Index `(Status, ExpiresAt)`, `(BuyerUserId)`, `(DestinationCountry, Status)`.

### Quotations (exporter offers)
`Id, TradeRequestId FK, SupplierBusinessId FK, Status (QuotationStatus), Currency,
**Incoterms, PortOfLoading, PortOfDischarge, LeadTimeDays, PaymentTerms**, Total,
Notes?, ValidUntil?, CreatedAtUtc, UpdatedAtUtc, RowVersion`.
Unique `(TradeRequestId, SupplierBusinessId)` — one active quote per supplier per RFQ.

### QuotationItems
`Id, QuotationId FK, ProductId? FK, ProductName, Quantity, UnitPrice, LineTotal`.

---

## Communication

### Messages
`Id, ThreadKey nvarchar(64), SenderUserId FK, RecipientUserId FK, ContextType
(Order/Rfq/Business/General), ContextId?, Body, AttachmentUrl?, IsRead bit,
SentAtUtc`. ThreadKey canonicalizes participants + context for ordered threads.
Attachments double as mini-documents (samples, spec sheets) in MVP.

### Notifications
`Id, UserId FK, Type (NotificationType), Title, Body, Link, IsRead bit, CreatedAtUtc`.
Index `(UserId, IsRead, CreatedAtUtc DESC)`.

---

## Logistics

### Shipments
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| OrderId | guid | FK Orders |
| CarrierBusinessId | guid? | FK Businesses (LSP) |
| Status | int | ShipmentStatus |
| Mode | int | Road/Rail/Sea/Air/Multimodal |
| OriginCountry / OriginPort | | |
| DestinationCountry / DestinationPort | | |
| Incoterms | int? | |
| DepartureDate / Eta | datetime2? | |
| TrackingReference | nvarchar(100) | carrier reference |
| PublicTrackingToken | nvarchar(64) | guest tracking link |

### ShipmentTrackingEvents
`Id, ShipmentId FK, Status int, Location?, Description?, EventTimeUtc`. Append-only.

---

## Documents (trade documents — core)

### Documents
| Column | Type | Notes |
| --- | --- | --- |
| Id | guid | PK |
| DocType | int | **DocumentType** |
| Title | nvarchar(200) | |
| FileUrl / FileName | | via IFileStorage |
| ContextType | int | Order / Rfq / Business / Quote / User |
| ContextId | guid | |
| Status | int | **Draft / Uploaded / UnderReview / Verified / Rejected / Expired** |
| UploadedByUserId | guid | |
| VerifiedByUserId? / VerifiedAtUtc? | | |
| Notes? | nvarchar(max) | rejection reason |
| CreatedAtUtc / UpdatedAtUtc | | |

Index `(ContextType, ContextId)`, `(Status, DocType)` for admin queues.

**Document-type library (trade-aligned):** CommercialInvoice, PackingList,
CertificateOfOrigin, BillOfLading, PhytosanitaryCertificate, InspectionCertificate,
ExportPermit, ImportPermit — plus business/KYC types: BusinessRegistration, TaxId,
IdDocument, ProofOfAddress, ProductCertification, Other. A `DestinationRequirements`
helper (Phase 4) will suggest required docs by origin/destination pair.

---

## Intelligence

### MarketData
`Id, Commodity, Country char(2), Region?, Unit, Price decimal(18,2), Currency, Source,
ReportedAtUtc, Notes?`. Append-only price series; unique
`(Commodity, Country, Region, Unit, Currency, ReportedAtUtc)` per import batch.
Phase 3+ powers commodity prices and trade-trend charts.

---

## Workflow enums (stored as int)

| Enum | Values |
| --- | --- |
| VerificationStatus | Unverified=0, Pending=1, Verified=2, Rejected=3, Suspended=4 |
| DocumentType | (trade + KYC list above) |
| DocumentStatus | Draft, Uploaded, UnderReview, Verified, Rejected, Expired |
| ListingStatus | Draft, Active, Inactive |
| OrderStatus | Pending, Confirmed, Processing, InTransit, Delivered, Completed, Cancelled, Refunded |
| PaymentStatus | Pending, Initiated, Authorized, Paid, Failed, Refunded, PartiallyRefunded |
| PaymentTerms | TtAdvance=1, TtPartialDeposit=2, LetterOfCredit=3, CashAgainstDocuments=4, OnAccount=5, Other=99 |
| RfqStatus | Open, Quoting, Negotiating, Accepted, ConvertedToOrder, Expired, Closed, Cancelled |
| QuotationStatus | Submitted, UnderReview, Negotiating, Accepted, Declined, Withdrawn, Expired |
| Incoterms | EXW, FCA, FAS, FOB, CFR, CIF, CPT, CIP, DAP, DPU, DDP |
| ShipmentStatus | Created, Booked, InTransit, CustomsHold, OutForDelivery, Delivered, Delayed, Cancelled |
| ShipmentMode | Road, Rail, Sea, Air, Multimodal |
| DisputeStatus | Open, UnderReview, Resolved, Rejected, Escalated |
| NotificationType | Order, Rfq, Quotation, Message, Payment, Shipment, Verification, Document, Dispute, System |

**Enums:** stored as `int`, appended only, never renumbered.

---

## Identity integration decision

Use **ASP.NET Core Identity** (`Users/Roles/UserRoles/RoleClaims/UserClaims/UserLogins/
UserTokens`) and extend `ApplicationUser` with business columns. Battle-tested password
hashing, lockout, email confirm, and refresh-token storage for free.

## Seeding plan

| Seed | Content |
| --- | --- |
| Roles | 9 roles from spec §6 |
| Administrator | Root admin (credentials from user-secrets, never committed) |
| Categories | 12 top-level categories from spec §4 + cashew/maize/soybean commodity children |
| HS-code starter map | Common commodities → HS codes (cashew 0801, maize 1005, soybeans 1201, cocoa 1801…) |
| Demo corridors | Verified exporters (Mombasa FOB cashew; Lagos CIF maize), verified importer (Ng/Lagos), LSP (sea freight) |
| Demo products | ~10 export listings with trade terms, certifications, stock |
| MarketData | 2–3 commodities × weeks of daily prices (Phase 3 charts) |

## Migration & schema management

- One EF migration per reviewed change; PR-reviewed.
- `dotnet ef migrations add X --project src/AgroTrade.Infrastructure --startup-project src/AgroTrade.Api --context AgroTradeDbContext`
- CI runs a schema-drift check before deploy.