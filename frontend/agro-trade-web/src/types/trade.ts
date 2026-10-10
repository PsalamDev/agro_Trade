// Wire-format mirrors of the Phase 1 API DTOs (plan §03 — Businesses, Categories,
// Products, RFQs, Messages, Notifications). Enums are ints on the wire; labels live here.

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

// ---- Enums (values mirror AgroTrade.Domain.Enums; append-only) ----

export const VERIFICATION_STATUSES = ['Unverified', 'Pending', 'Verified', 'Rejected', 'Suspended'] as const
export const VERIFICATION_STATUS = { Unverified: 0, Pending: 1, Verified: 2, Rejected: 3, Suspended: 4 } as const

export const BUSINESS_TYPES = ['Sole proprietor', 'Partnership', 'Limited company', 'Cooperative', 'NGO', 'Other'] as const
export const BUSINESS_TYPE = { SoleProprietor: 0, Partnership: 1, LimitedCompany: 2, Cooperative: 3, Ngo: 4, Other: 5 } as const

export const INCOTERMS = ['EXW', 'FCA', 'FAS', 'FOB', 'CFR', 'CIF', 'CPT', 'CIP', 'DAP', 'DPU', 'DDP'] as const
export const INCOTERM = { Exw: 0, Fca: 1, Fas: 2, Fob: 3, Cfr: 4, Cif: 5, Cpt: 6, Cip: 7, Dap: 8, Dpu: 9, Ddp: 10 } as const

export const PRICE_UNITS = ['Per kilogram', 'Per ton', 'Per bag', 'Per carton', 'Per crate', 'Per unit', 'Per litre', 'Per head', 'Per container', 'Other'] as const
export const PRICE_UNIT = { PerKilogram: 0, PerTon: 1, PerBag: 2, PerCarton: 3, PerCrate: 4, PerUnit: 5, PerLitre: 6, PerHead: 7, PerContainer: 8, Other: 9 } as const

export const LISTING_STATUSES = ['Draft', 'Active', 'Inactive'] as const
export const LISTING_STATUS = { Draft: 0, Active: 1, Inactive: 2 } as const

export const RFQ_STATUSES = ['Open', 'Quoting', 'Negotiating', 'Accepted', 'Converted to order', 'Expired', 'Closed', 'Cancelled'] as const
export const RFQ_STATUS = { Open: 0, Quoting: 1, Negotiating: 2, Accepted: 3, ConvertedToOrder: 4, Expired: 5, Closed: 6, Cancelled: 7 } as const

export const QUOTATION_STATUSES = ['Submitted', 'Under review', 'Negotiating', 'Accepted', 'Declined', 'Withdrawn', 'Expired'] as const
export const QUOTATION_STATUS = { Submitted: 0, UnderReview: 1, Negotiating: 2, Accepted: 3, Declined: 4, Withdrawn: 5, Expired: 6 } as const

export const PAYMENT_TERMS = ['T/T advance', 'T/T partial deposit', 'Letter of credit', 'Cash against documents', 'On account', 'Other'] as const
export const PAYMENT_TERM = { TtAdvance: 1, TtPartialDeposit: 2, LetterOfCredit: 3, CashAgainstDocuments: 4, OnAccount: 5, Other: 99 } as const

export const SHIPMENT_MODES = ['Road', 'Rail', 'Sea', 'Air', 'Multimodal'] as const
export const SHIPMENT_MODE = { Road: 0, Rail: 1, Sea: 2, Air: 3, Multimodal: 4 } as const

export const NOTIFICATION_TYPES = ['Order', 'RFQ', 'Quotation', 'Message', 'Payment', 'Shipment', 'Verification', 'Document', 'Dispute', 'System', 'Review', 'Refund'] as const
export const NOTIFICATION_TYPE = { Order: 0, Rfq: 1, Quotation: 2, Message: 3, Payment: 4, Shipment: 5, Verification: 6, Document: 7, Dispute: 8, System: 9, Review: 10, Refund: 11 } as const

export const MESSAGE_CONTEXT_TYPES = ['Order', 'RFQ', 'Business', 'General'] as const
export const MESSAGE_CONTEXT_TYPE = { Order: 0, Rfq: 1, Business: 2, General: 3 } as const

export const DOCUMENT_TYPES = [
  'Commercial invoice',
  'Packing list',
  'Certificate of origin',
  'Bill of lading',
  'Phytosanitary certificate',
  'Inspection certificate',
  'Export permit',
  'Import permit',
  'Business registration',
  'Tax ID',
  'ID document',
  'Proof of address',
  'Product certification',
  'Other',
] as const
export const DOCUMENT_TYPE = {
  CommercialInvoice: 0,
  PackingList: 1,
  CertificateOfOrigin: 2,
  BillOfLading: 3,
  PhytosanitaryCertificate: 4,
  InspectionCertificate: 5,
  ExportPermit: 6,
  ImportPermit: 7,
  BusinessRegistration: 10,
  TaxId: 11,
  IdDocument: 12,
  ProofOfAddress: 13,
  ProductCertification: 14,
  Other: 99,
} as const

/**
 * Document types that may attach to an order, as label/value pairs.
 *
 * A narrower set than DOCUMENT_TYPES: a trade document is paperwork for one shipment, so the
 * KYC types (business registration, tax ID, proof of address) are not offered — those belong
 * to business verification, which is a separate flow with its own queue.
 *
 * Pairs rather than a positional label list because the wire values are not contiguous: the
 * server numbers these 0-7 with 99 for 'Other', so indexing a dense array by the value would
 * label every 'Other' as something else.
 */
export const TRADE_DOCUMENT_TYPE_OPTIONS = [
  { value: 0, label: 'Commercial invoice' },
  { value: 1, label: 'Packing list' },
  { value: 2, label: 'Certificate of origin' },
  { value: 3, label: 'Bill of lading' },
  { value: 4, label: 'Phytosanitary certificate' },
  { value: 5, label: 'Inspection certificate' },
  { value: 6, label: 'Export permit' },
  { value: 7, label: 'Import permit' },
  { value: 99, label: 'Other' },
] as const

export const TRADE_DOCUMENT_TYPE_LABELS = TRADE_DOCUMENT_TYPE_OPTIONS.map((o) => o.label)
export const TRADE_DOCUMENT_TYPE_VALUES = TRADE_DOCUMENT_TYPE_OPTIONS.map((o) => o.value)

/** Label for a trade document type value; unknown values render as their number. */
export function tradeDocumentLabel(value: number): string {
  return TRADE_DOCUMENT_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? `Unknown (${value})`
}

export const DOCUMENT_STATUSES = ['Draft', 'Uploaded', 'Under review', 'Verified', 'Rejected', 'Expired'] as const
export const DOCUMENT_STATUS = {
  Draft: 0,
  Uploaded: 1,
  UnderReview: 2,
  Verified: 3,
  Rejected: 4,
  Expired: 5,
} as const

// ---- Orders (task 2.1 + 2.2; plan §02 — Orders) ----

export const ORDER_STATUSES = ['Draft', 'Placed', 'Confirmed', 'In transit', 'Delivered', 'Completed', 'Cancelled'] as const
export const ORDER_STATUS = {
  Draft: 0,
  Placed: 1,
  Confirmed: 2,
  InTransit: 3,
  Delivered: 4,
  Completed: 5,
  Cancelled: 6,
} as const

export type OrderStatusValue = (typeof ORDER_STATUS)[keyof typeof ORDER_STATUS]

export const PAYMENT_STATUSES = ['No payment due', 'Payment pending', 'Part paid', 'Paid', 'Refunded', 'Payment failed'] as const
export const PAYMENT_STATUS = {
  None: 0,
  Pending: 1,
  PartiallyPaid: 2,
  Paid: 3,
  Refunded: 4,
  Failed: 5,
} as const

/** Which side of an order the signed-in user is. */
export type OrderParty = 'buyer' | 'seller' | 'outsider'

/**
 * Client mirror of the server's OrderLifecycle rules. The server is authoritative — it
 * re-checks every transition and answers 409/403 if this table drifts — but the UI has to
 * know which buttons to render, so the rules are mirrored here rather than inferred.
 *
 * Cancel is offered to either party and only before the goods are in transit: once a
 * consignment has sailed, unwinding it is a dispute rather than a status change.
 */
const ORDER_ACTIONS: Record<number, ReadonlyArray<{ to: OrderStatusValue; parties: readonly OrderParty[]; label: string; destructive?: boolean }>> = {
  [ORDER_STATUS.Draft]: [
    { to: ORDER_STATUS.Placed, parties: ['buyer'], label: 'Place order' },
    { to: ORDER_STATUS.Cancelled, parties: ['buyer', 'seller'], label: 'Cancel order', destructive: true },
  ],
  [ORDER_STATUS.Placed]: [
    { to: ORDER_STATUS.Confirmed, parties: ['seller'], label: 'Confirm order' },
    { to: ORDER_STATUS.Cancelled, parties: ['buyer', 'seller'], label: 'Cancel order', destructive: true },
  ],
  [ORDER_STATUS.Confirmed]: [
    { to: ORDER_STATUS.InTransit, parties: ['seller'], label: 'Mark as dispatched' },
    { to: ORDER_STATUS.Cancelled, parties: ['buyer', 'seller'], label: 'Cancel order', destructive: true },
  ],
  [ORDER_STATUS.InTransit]: [{ to: ORDER_STATUS.Delivered, parties: ['seller'], label: 'Mark as delivered' }],
  [ORDER_STATUS.Delivered]: [{ to: ORDER_STATUS.Completed, parties: ['buyer'], label: 'Confirm receipt' }],
  [ORDER_STATUS.Completed]: [],
  [ORDER_STATUS.Cancelled]: [],
}

/** Transitions the given party may drive from the current status. */
export function orderActions(status: number, party: OrderParty): ReadonlyArray<{ to: OrderStatusValue; label: string; destructive?: boolean }> {
  if (party === 'outsider') return []
  return (ORDER_ACTIONS[status] ?? []).filter((a) => a.parties.includes(party))
}

export function labelOf(list: readonly string[], value: number): string {
  return list[value] ?? `Unknown (${value})`
}

// ---- Businesses ----

export interface BusinessSummaryDto {
  id: string
  tradingName: string
  legalName: string
  businessType: number
  verificationStatus: number
  isVerifiedBadgeVisible: boolean
  rating: number
  ratingCount: number
  categoryFocusId: string | null
  exportsTo: string[]
  importsFrom: string[]
  isOwner: boolean
}

export interface BusinessDetailDto extends BusinessSummaryDto {
  ownerUserId: string
  website: string | null
  description: string | null
  verification: VerificationStateDto | null
  documents: BusinessDocumentDto[]
  productIds: string[]
}

export interface VerificationStateDto {
  status: number
  isBadgeVisible: boolean
  verifiedLevel: string
  requestedAtUtc: string | null
  reviewedAtUtc: string | null
  notes: string | null
  history: VerificationHistoryDto[]
}

export interface VerificationHistoryDto {
  status: number
  requestedAtUtc: string
  reviewedAtUtc: string | null
  notes: string | null
}

export interface BusinessDocumentDto {
  id: string
  docType: number
  fileName: string
  fileUrl: string
  uploadedAtUtc: string
}

export interface RegisterBusinessRequest {
  legalName: string
  tradingName: string
  registrationNumber?: string
  businessType: number
  categoryFocusId?: string
  website?: string
  description?: string
}

export interface UpdateBusinessRequest {
  tradingName?: string
  website?: string
  description?: string
}

export interface TradeCountriesRequest {
  exportsTo: string[]
  importsFrom: string[]
}

export interface ReviewVerificationRequest {
  approve: boolean
  notes?: string
}

export interface BusinessSearchParams {
  q?: string
  country?: string
  type?: number
  verifiedOnly?: boolean
  exportsTo?: string
  importsFrom?: string
  page?: number
  pageSize?: number
}

// ---- Catalog ----

export interface CategoryDto {
  id: string
  parentId: string | null
  name: string
  slug: string
  icon: string | null
  sortOrder: number
  children: CategoryDto[]
}

export interface HsCodeEntryDto {
  commodity: string
  hsCode: string
}

export interface CountryDto {
  name: string
  alpha2: string
  alpha3: string
  numeric: string
}

// ---- Products ----

export interface ProductCardDto {
  id: string
  slug: string
  name: string
  hsCode: string | null
  originCountry: string
  portOfLoading: string | null
  price: number
  priceUnit: number
  currency: string
  minimumOrderQuantity: number
  availableQuantity: number
  incoterms: number | null
  leadTimeDays: number | null
  listingStatus: number
  verificationStatus: number
  isVerified: boolean
  sellerBusinessId: string
  sellerTradingName: string
  isSellerVerifiedBadgeVisible: boolean
  primaryImageUrl: string | null
  categoryId: string
  createdAtUtc: string | null
}

export interface ProductDetailDto extends ProductCardDto {
  description: string | null
  paymentTermsOffered: string[]
  qualitySpecs: Record<string, string>
  packagingOptions: string[]
  certifications: string[]
  images: ProductImageDto[]
  seller: SellerCardDto
}

export interface ProductImageDto {
  id: string
  url: string
  altText: string | null
  sortOrder: number
  isPrimary: boolean
}

export interface SellerCardDto {
  businessId: string
  tradingName: string
  verificationStatus: number
  isVerifiedBadgeVisible: boolean
  rating: number
  ratingCount: number
  homeCountry: string | null
}

export interface CreateProductRequest {
  name: string
  categoryId: string
  hsCode?: string
  originCountry: string
  portOfLoading?: string
  price: number
  priceUnit: number
  currency: string
  availableQuantity: number
  minimumOrderQuantity: number
  incoterms?: number
  leadTimeDays?: number
  paymentTermsOffered: string[]
  qualitySpecs: Record<string, string>
  packagingOptions: string[]
  certifications: string[]
  listingStatus: number
}

export interface UpdateProductRequest {
  name?: string
  description?: string
  hsCode?: string
  portOfLoading?: string
  price?: number
  priceUnit?: number
  currency?: string
  availableQuantity?: number
  minimumOrderQuantity?: number
  incoterms?: number
  leadTimeDays?: number
  listingStatus?: number
}

export interface ProductSearchParams {
  q?: string
  categoryId?: string
  hsCode?: string
  originCountry?: string
  exportsToCountry?: string
  incoterms?: number
  currency?: string
  minPrice?: number
  maxPrice?: number
  verifiedOnly?: boolean
  sellerBusinessId?: string
  sortBy?: string
  sortDir?: string
  page?: number
  pageSize?: number
}

// ---- RFQs & quotations ----

export interface TradeRequestCardDto {
  id: string
  rfqNumber: string
  title: string
  categoryId: string | null
  quantity: number
  unit: number
  destinationCountry: string
  destinationPortOrCity: string | null
  preferredIncoterm: number | null
  transportMode: number | null
  preferredCurrency: string
  status: number
  expiresAt: string
  createdAtUtc: string | null
  quotationCount: number
  isMine: boolean
  canQuote: boolean
  certificationsRequired: string[]
}

export interface TradeRequestDetailDto extends TradeRequestCardDto {
  description: string | null
  insuranceRequired: boolean
  targetSpecs: Record<string, string>
  paymentTermsPreferred: number | null
  deliveryDeadline: string | null
  budgetMin: number | null
  budgetMax: number | null
  buyerDisplayName: string
  isBuyer: boolean
  quotations: QuotationDto[]
}

export interface QuotationDto {
  id: string
  tradeRequestId: string
  supplierBusinessId: string
  supplierOwnerUserId: string
  supplierTradingName: string
  isSupplierVerifiedBadgeVisible: boolean
  status: number
  currency: string
  incoterms: number
  portOfLoading: string | null
  portOfDischarge: string | null
  leadTimeDays: number
  paymentTerms: number
  total: number
  notes: string | null
  validUntil: string | null
  createdAtUtc: string | null
  isMine: boolean
}

export interface CreateTradeRequestRequest {
  title: string
  description?: string
  categoryId?: string
  quantity: number
  unit: number
  destinationCountry: string
  destinationPortOrCity?: string
  preferredIncoterm?: number
  transportMode?: number
  insuranceRequired: boolean
  targetSpecs: Record<string, string>
  certificationsRequired: string[]
  preferredCurrency: string
  paymentTermsPreferred?: number
  deliveryDeadline?: string
  budgetMin?: number
  budgetMax?: number
  expiresAt: string
}

export interface CreateQuotationRequest {
  currency: string
  incoterms: number
  portOfLoading?: string
  portOfDischarge?: string
  leadTimeDays: number
  paymentTerms: number
  total: number
  notes?: string
  validUntil?: string
  businessId?: string
}

export interface RfqListParams {
  as?: 'buyer' | 'supplier'
  status?: number
  page?: number
  pageSize?: number
}

// ---- Messages & notifications ----

export interface ConversationDto {
  threadKey: string
  otherUserId: string
  otherDisplayName: string
  contextType: number
  contextId: string | null
  lastMessage: string
  lastMessageAtUtc: string
  unreadCount: number
  totalMessages: number
}

export interface MessageDto {
  id: string
  threadKey: string
  senderUserId: string
  senderDisplayName: string
  recipientUserId: string
  contextType: number
  contextId: string | null
  body: string
  isRead: boolean
  sentAtUtc: string
}

export interface SendMessageRequest {
  body: string
  contextType?: number
  contextId?: string
  recipientUserId?: string
}

export interface NotificationDto {
  id: string
  type: number
  title: string
  body: string | null
  link: string | null
  isRead: boolean
  createdAtUtc: string
}

// ---- Orders ----

export interface OrderItemDto {
  id: string
  /** Null for lines raised from a quotation: a consignment, not a catalog listing. */
  productId: string | null
  productNameSnapshot: string
  productSkuSnapshot: string | null
  quantity: number
  unit: number
  unitPrice: number
  lineTotal: number
  currency: string
}

export interface OrderCardDto {
  id: string
  orderNumber: string
  status: number
  paymentStatus: number
  currency: string
  total: number
  sellerTradingName: string
  buyerDisplayName: string
  incoterms: number
  portOfLoading: string | null
  portOfDischarge: string | null
  itemCount: number
  expectedDeliveryUtc: string | null
  placedAtUtc: string | null
  createdAtUtc: string | null
}

export interface OrderDetailDto {
  id: string
  orderNumber: string
  quotationId: string | null
  tradeRequestId: string | null
  rfqNumber: string | null
  status: number
  paymentStatus: number
  buyerUserId: string
  buyerDisplayName: string
  sellerBusinessId: string
  sellerTradingName: string
  /**
   * Which side the viewer is on, resolved server-side from actual business ownership. Prefer
   * these over inferring from the Company role: a Company user who owns a different business is
   * still an outsider here.
   */
  isBuyer: boolean
  isSeller: boolean
  currency: string
  subtotal: number
  discount: number
  total: number
  amountPaid: number
  balanceDue: number
  incoterms: number
  portOfLoading: string | null
  portOfDischarge: string | null
  paymentTerms: number
  destinationCountry: string | null
  transportMode: number | null
  destinationAddressId: string | null
  expectedDeliveryUtc: string | null
  placedAtUtc: string | null
  completedAtUtc: string | null
  createdAtUtc: string | null
  updatedAtUtc: string | null
  notes: string | null
  items: OrderItemDto[]
  /** Gateway and off-platform payments (task 2.4 + 2.5). */
  payments: PaymentDto[]
  documents: TradeDocumentDto[]
}

// ---- Trade documents (task 2.3) ----

export interface TradeDocumentDto {
  id: string
  orderId: string
  docType: number
  /** Reference printed on the paper, e.g. "INV-2026-0042". */
  documentReference: string | null
  fileUrl: string | null
  fileName: string | null
  fileSizeBytes: number | null
  contentType: string | null
  status: number
  reviewNotes: string | null
  reviewedByUserId: string | null
  reviewedAtUtc: string | null
  issuedOnUtc: string | null
  expiresOnUtc: string | null
  createdAtUtc: string | null
  /** Present on the order page and the admin queue; empty on an order-scoped read of a document. */
  orderNumber: string
  orderCounterparty: string | null
  uploadedByUserId: string | null
  uploadedByDisplayName: string | null
}

export const PAYMENT_PROVIDERS = ['Recorded off-platform', 'Sandbox gateway'] as const
export const PAYMENT_PROVIDER = {
  ManualTransfer: 0,
  SandboxGateway: 1,
} as const

export const PAYMENT_METHODS = ['Bank transfer', 'Letter of credit', 'Card / gateway'] as const
export const PAYMENT_METHOD = {
  BankTransfer: 0,
  LetterOfCredit: 1,
  Gateway: 2,
} as const

/** Outcomes the sandbox charge can be driven to. Kept in step with the server. */
export const SIMULATE_OUTCOMES = ['succeeded', 'failed'] as const
export type SimulateOutcome = (typeof SIMULATE_OUTCOMES)[number]

export const INVOICE_STATUSES = ['Draft', 'Issued', 'Overdue', 'Paid', 'Cancelled'] as const
export const INVOICE_STATUS = {
  Draft: 0,
  Issued: 1,
  Overdue: 2,
  Paid: 3,
  Cancelled: 4,
} as const

/**
 * The document a seller issues against a placed order so the buyer knows what to pay. One per
 * order, for the order's total. `Paid` when the order's payments cover it in full.
 */
export interface InvoiceDto {
  id: string
  orderId: string
  invoiceNumber: string
  amount: number
  currency: string
  status: number
  issuedAtUtc: string
  dueDateUtc: string | null
  paidAtUtc: string | null
}

/** Bodies for issuing an invoice. `orderId` comes from the route. */
export interface GenerateInvoiceRequest {
  dueDateUtc?: string
  notes?: string
}

/**
 * The evidence that a specific settled payment arrived. Written automatically by settlement —
 * this screen only reads it.
 */
export interface ReceiptDto {
  id: string
  orderId: string
  paymentId: string | null
  invoiceId: string | null
  receiptNumber: string
  amount: number
  currency: string
  issuedAtUtc: string
  notes: string | null
}

export interface PaymentDto {
  id: string
  orderId: string
  orderNumber: string
  reference: string
  amount: number
  currency: string
  provider: number
  method: number
  status: number
  /** The gateway's charge id. Null for a manual transfer, which has no gateway. */
  providerRef: string | null
  remittanceReference: string | null
  bankName: string | null
  valueDateUtc: string | null
  initiatedAtUtc: string
  paidAtUtc: string | null
  /** When the payment was refunded in full. Null until nothing is left to refund. */
  refundedAtUtc: string | null
  /** How much of this payment has been refunded so far, as a positive number. */
  refundedAmount: number | null
  failureReason: string | null
  notes: string | null
  /** Where the payer completes the charge. Sandbox only; null once settled. */
  nextActionUrl: string | null
  /** Outstanding against the order, on the list view only. */
  orderBalanceDue: number | null
}

/**
 * Caller-supplied retry guard. Repeating an initiation with the same key returns the original
 * payment instead of raising a second charge, so a request that times out can simply be retried.
 * Generate one per attempt and reuse it across retries.
 */
export interface InitiatePaymentRequest {
  /** Defaults to the order's outstanding balance. */
  amount?: number
  idempotencyKey?: string
  notes?: string
}

/**
 * An administrator refunding money back on a settled payment.
 *
 * `amount` defaults to everything the payment still has refundable; it can never exceed that,
 * so a retried or doubled request is refused rather than silently over-refunding. `reason` is
 * optional and is recorded on the ledger entry and in the audit trail.
 */
export interface RefundPaymentRequest {
  amount?: number
  reason?: string
}

export const TRANSACTION_TYPES = ['Payment', 'Refund', 'Fee'] as const
export const TRANSACTION_TYPE = {
  Payment: 0,
  Refund: 1,
  Fee: 2,
} as const

/**
 * One immutable ledger entry. Where `PaymentDto` is a mutable attempt, this is the financial
 * history: written once when money moves, never edited.
 */
export interface TransactionDto {
  id: string
  orderId: string
  reference: string
  type: number
  /** Positive for money in, negative for money out. */
  amount: number
  currency: string
  paymentId: string | null
  paymentReference: string | null
  /** The counterparty's own reference — a gateway charge id, or a bank's trace id. */
  externalReference: string | null
  occurredAtUtc: string
  narrative: string | null
}

/**
 * An off-platform payment the seller confirms has landed.
 *
 * Recorded as already settled: there is no provider to ask, so a pending state here would mean
 * only "waiting for someone to come back and type something".
 */
export interface RecordManualPaymentRequest {
  amount: number
  method: number
  /** The bank's trace id. Required, and unique per order. */
  remittanceReference: string
  bankName?: string
  /** When the funds landed. Defaults to now. Must not be in the future. Send UTC. */
  valueDateUtc?: string
  notes?: string
  idempotencyKey?: string
}

/** Fields the upload form collects. File metadata comes from the validated upload. */
export interface UploadTradeDocumentRequest {
  docType: number
  documentReference?: string
  issuedOnUtc?: string
  expiresOnUtc?: string
}

export interface ReviewDocumentRequest {
  status: number
  /** Required by the server when rejecting. */
  notes?: string
}

export interface DocumentSearchParams {
  status?: number
  docType?: number
  q?: string
  page?: number
  pageSize?: number
}

export interface OrderTransitionRequest {
  to: number
  reason?: string
}

export interface OrderListParams {
  status?: number
  page?: number
  pageSize?: number
}

// ---- Shipments (task 2.6) ----

/**
 * Where the consignment is. The server derives this from the latest milestone rather than
 * storing it, so every status shown here is backed by a dated, located event in the timeline.
 */
export const SHIPMENT_STATUSES = [
  'Pending',
  'Booked',
  'Departed',
  'In transit',
  'Arrived',
  'Delivered',
  'Exception',
] as const
export const SHIPMENT_STATUS = {
  Pending: 0,
  Booked: 1,
  Departed: 2,
  InTransit: 3,
  Arrived: 4,
  Delivered: 5,
  Exception: 6,
} as const

export type ShipmentStatusName = (typeof SHIPMENT_STATUSES)[number]

export interface ShipmentTrackingEventDto {
  id: string
  shipmentId: string
  status: number
  location: string | null
  description: string | null
  /** When the milestone happened, per the carrier. */
  occurredAtUtc: string
  /** When someone typed it in. Different from `occurredAtUtc` on a late entry. */
  recordedAtUtc: string
  recordedByDisplayName: string | null
}

/** A consignment, for the two parties and admins. */
export interface ShipmentDto {
  id: string
  orderId: string
  orderNumber: string
  /** e.g. "SHP-2026-000001". Carries no order or party information, so it is safe to share. */
  reference: string
  status: number
  statusSinceUtc: string | null
  mode: number
  carrierName: string
  /** Container number, air waybill or bill of lading. */
  trackingReference: string | null
  originCountry: string | null
  originPort: string | null
  destinationCountry: string | null
  destinationPort: string | null
  incoterms: number | null
  /** Derived from the earliest Departed milestone. */
  departedAtUtc: string | null
  estimatedArrivalUtc: string | null
  /**
   * Whether a guest link exists. The link itself is never readable again - the server stores only
   * its hash - so a seller who loses the URL rotates it.
   */
  hasGuestTracking: boolean
  isSeller: boolean
  isBuyer: boolean
  /** Newest first. */
  events: ShipmentTrackingEventDto[]
}

/** Fields the book-a-consignment form collects. Ports default to the order's own. */
export interface CreateShipmentRequest {
  mode: number
  carrierName: string
  trackingReference?: string
  originCountry?: string
  originPort?: string
  destinationCountry?: string
  destinationPort?: string
  incoterms?: number
  estimatedArrivalUtc?: string
  /** Omitted means "Booked" at the origin. */
  initialStatus?: string
  initialNote?: string
  /**
   * When the consignment was booked, if not today. Needed when logging up a shipment after the
   * fact: every milestone is sorted against this date.
   */
  initialOccurredAtUtc?: string
  /** On by default. Off means no public tracking link is issued. */
  enableGuestTracking?: boolean
}

export interface AppendTrackingEventRequest {
  status: number
  location?: string
  description?: string
  /** Defaults to now. Must not be in the future. */
  occurredAtUtc?: string
}

/** The one and only place a guest tracking token is ever returned. */
export interface TrackingTokenDto {
  shipmentId: string
  token: string
  trackingPath: string
}

/**
 * The anonymous guest view. Deliberately narrow: route, carrier, reference, ETA and milestones,
 * with no order, parties, goods or money. Someone holding a forwarded link needs to know where the
 * container is, and has no business learning who traded what for how much.
 */
export interface ShipmentPublicTrackingDto {
  reference: string
  status: number
  mode: number
  carrierName: string
  trackingReference: string | null
  originCountry: string | null
  originPort: string | null
  destinationCountry: string | null
  destinationPort: string | null
  incoterms: number | null
  departedAtUtc: string | null
  estimatedArrivalUtc: string | null
  /** Oldest first, so the page reads forwards as a journey. */
  events: {
    status: number
    location: string | null
    description: string | null
    occurredAtUtc: string
  }[]
}
// ---- Admin dashboard and audit trail (task 2.7) ----

/**
 * One stage of a count-by-status breakdown.
 *
 * `label` arrives from the server rather than being looked up here: the funnel is the one place an
 * admin reads a stage name without also having the status value to hand. Ordinary lists keep their
 * client-side labels, since those rows carry the enum value alongside.
 */
export interface AdminFunnelStageDto {
  status: number
  label: string
  count: number
}

/**
 * Trade value for one corridor in one currency.
 *
 * `currency` is part of this row's identity, not a label. There is no FX rate anywhere in the
 * platform, so these rows are never summed across currencies — `netSettled` and `grossOrderValue`
 * describe money in one unit each, and `outstanding` is the gap for that unit alone.
 */
export interface AdminCorridorStatDto {
  originPort: string | null
  destinationCountry: string | null
  currency: string
  orderCount: number
  /** Value ordered on this corridor, paid or not. */
  grossOrderValue: number
  /** Money that actually arrived, from the ledger rather than from an order rollup. */
  netSettled: number
  /** Gross still outstanding. Negative where the corridor has been over-refunded. */
  outstanding: number
  /** Always 0 in practice. Non-zero means a ledger entry was booked in the wrong currency. */
  mismatchedLedgerEntries: number
}

/** Supply and demand for one category. A row can exist with demand and no listings — that is the gap. */
export interface AdminCommodityStatDto {
  categoryName: string
  productCount: number
  /** Published only, so a draft never reads as supply a buyer can act on. */
  publishedCount: number
  /** Every RFQ ever filed — lasting interest in the category. */
  rfqCount: number
  /** RFQs still open or being quoted. What the gap flag is based on: interest nobody acted on years ago is not demand. */
  openRfqCount: number
}

export interface AdminDemandStatDto {
  openRfqs: number
  totalRfqs: number
  /** Open RFQs with no quotation at all — demand the seller is not meeting. */
  unansweredOpenRfqs: number
  /** Null when no open RFQ states a budget, or when they disagree on currency: an average across currencies would be meaningless. */
  averageBudgetCeiling: number | null
  /**
   * The single currency open RFQs agree on, `'Mixed'` when they disagree, or `''` when none are
   * open. Empty is not a flavour of mixed — nothing open is a different fact from disagreement.
   */
  preferredCurrency: string
}

export interface PlatformCountsDto {
  users: number
  businesses: number
  verifiedBusinesses: number
  products: number
  publishedProducts: number
  tradeRequests: number
  quotations: number
  orders: number
  shipments: number
  transactions: number
  /** Consignments that have hit an exception milestone at any point. */
  shipmentsInException: number
}

export interface AdminOverviewDto {
  counts: PlatformCountsDto
  businessVerificationFunnel: AdminFunnelStageDto[]
  documentVerificationFunnel: AdminFunnelStageDto[]
  orderFunnel: AdminFunnelStageDto[]
  paymentFunnel: AdminFunnelStageDto[]
  corridors: AdminCorridorStatDto[]
  commodities: AdminCommodityStatDto[]
  demand: AdminDemandStatDto
}

/** One administrative action. Append-only — there is no endpoint that edits or deletes these. */
export interface AdminAuditLogDto {
  id: string
  userId: string
  /** Null if the account has since been deleted; the id above still identifies who acted. */
  actorDisplayName: string | null
  action: string
  entityType: string
  entityId: string | null
  summary: string
  /** The reason given, where the action took one. Rejection notes live here. */
  details: string | null
  occurredAtUtc: string
}

/** Audit actions the server can currently write. A filter, not an exhaustive contract. */
export const AUDIT_ACTIONS = [
  { value: 'business.verification.approved', label: 'Business approved' },
  { value: 'business.verification.rejected', label: 'Business rejected' },
  { value: 'document.review.verified', label: 'Document verified' },
  { value: 'document.review.rejected', label: 'Document rejected' },
] as const

export interface AdminUserDto {
  id: string
  email: string
  displayName: string | null
  emailConfirmed: boolean
  lockedOut: boolean
  roles: string[]
  businessName: string | null
  createdAtUtc: string | null
}

export interface AdminOrderRowDto {
  id: string
  orderNumber: string
  status: number
  paymentStatus: number
  currency: string
  total: number
  amountPaid: number
  buyerDisplayName: string | null
  sellerTradingName: string | null
  destinationCountry: string | null
  placedAtUtc: string | null
  createdAtUtc: string | null
  shipmentCount: number
}

export interface AdminShipmentDto {
  id: string
  reference: string
  orderId: string
  orderNumber: string
  /** Derived from the latest milestone, exactly as the parties see it. Never a stored column. */
  status: number
  statusSinceUtc: string | null
  mode: number
  carrierName: string
  destinationCountry: string | null
  /** A live guest token is a bearer secret being handed to people with no account. */
  hasGuestTracking: boolean
  milestoneCount: number
  estimatedArrivalUtc: string | null
  createdAtUtc: string | null
}
