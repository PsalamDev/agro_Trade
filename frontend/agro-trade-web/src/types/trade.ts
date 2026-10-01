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

export const NOTIFICATION_TYPES = ['Order', 'RFQ', 'Quotation', 'Message', 'Payment', 'Shipment', 'Verification', 'Document', 'Dispute', 'System'] as const
export const NOTIFICATION_TYPE = { Order: 0, Rfq: 1, Quotation: 2, Message: 3, Payment: 4, Shipment: 5, Verification: 6, Document: 7, Dispute: 8, System: 9 } as const

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
  /** Empty until task 2.5 records off-platform payments. */
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

export interface PaymentDto {
  id: string
  orderId: string
  reference: string
  amount: number
  currency: string
  provider: number
  method: number
  status: number
  remittanceReference: string | null
  bankName: string | null
  valueDateUtc: string | null
  initiatedAtUtc: string
  paidAtUtc: string | null
  failureReason: string | null
  notes: string | null
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