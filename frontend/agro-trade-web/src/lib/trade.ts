import axios from 'axios'
import { api, problemFrom } from './api'
import type {
  AppendTrackingEventRequest,
  BusinessDetailDto,
  BusinessDocumentDto,
  BusinessSearchParams,
  BusinessSummaryDto,
  CategoryDto,
  ConversationDto,
  CreateProductRequest,
  CreateQuotationRequest,
  CreateShipmentRequest,
  CreateTradeRequestRequest,
  DocumentSearchParams,
  HsCodeEntryDto,
  InitiatePaymentRequest,
  MessageDto,
  NotificationDto,
  OrderCardDto,
  OrderDetailDto,
  OrderListParams,
  OrderTransitionRequest,
  PagedResult,
  ProductCardDto,
  ProductDetailDto,
  ProductImageDto,
  ProductSearchParams,
  PaymentDto,
  QuotationDto,
  RecordManualPaymentRequest,
  RegisterBusinessRequest,
  ReviewDocumentRequest,
  ReviewVerificationRequest,
  RfqListParams,
  SendMessageRequest,
  ShipmentDto,
  ShipmentPublicTrackingDto,
  ShipmentTrackingEventDto,
  TrackingTokenDto,
  TradeCountriesRequest,
  TradeDocumentDto,
  TradeRequestCardDto,
  TradeRequestDetailDto,
  TransactionDto,
  UpdateBusinessRequest,
  UploadTradeDocumentRequest,
  UpdateProductRequest,
  VerificationStateDto,
} from '../types/trade'

/** Await this and it throws problemFrom(...) on failure. */
async function unwrap<T>(request: Promise<{ data: T }>): Promise<T> {
  try {
    const { data } = await request
    return data
  } catch (error) {
    throw problemFrom(error)
  }
}

function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const qs = search.toString()
  return qs ? `?${qs}` : ''
}

// ---- Categories & HS codes ----

export function getCategoryTree(): Promise<CategoryDto[]> {
  return unwrap(api.get<CategoryDto[]>('/categories'))
}

export function getHsCodes(): Promise<HsCodeEntryDto[]> {
  return unwrap(api.get<HsCodeEntryDto[]>('/commodities/hs-codes'))
}

// ---- Businesses ----

export interface CreateBusinessOptions {
  business?: string
}

export function searchBusinesses(params: BusinessSearchParams = {}): Promise<PagedResult<BusinessSummaryDto>> {
  return unwrap(api.get<PagedResult<BusinessSummaryDto>>(`/businesses${query({ ...params })}`))
}

export function getBusiness(id: string): Promise<BusinessDetailDto> {
  return unwrap(api.get<BusinessDetailDto>(`/businesses/${id}`))
}

export function getMyBusinesses(): Promise<BusinessSummaryDto[]> {
  return unwrap(api.get<BusinessSummaryDto[]>('/businesses/mine'))
}

export function createBusiness(request: RegisterBusinessRequest): Promise<BusinessDetailDto> {
  return unwrap(api.post<BusinessDetailDto>('/businesses', request))
}

export function updateBusiness(id: string, request: UpdateBusinessRequest): Promise<BusinessDetailDto> {
  return unwrap(api.put<BusinessDetailDto>(`/businesses/${id}`, request))
}

export function setTradeCountries(id: string, request: TradeCountriesRequest): Promise<BusinessDetailDto> {
  return unwrap(api.put<BusinessDetailDto>(`/businesses/${id}/trade-countries`, request))
}

/** Upload a KYC document (multipart). docType is a DocumentType int. */
export function uploadBusinessDocument(
  id: string,
  docType: number,
  file: File,
): Promise<BusinessDocumentDto> {
  const form = new FormData()
  form.append('file', file)
  form.append('docType', String(docType))
  return unwrap(api.post<BusinessDocumentDto>(`/businesses/${id}/documents`, form))
}

export function getBusinessDocuments(id: string): Promise<BusinessDocumentDto[]> {
  return unwrap(api.get<BusinessDocumentDto[]>(`/businesses/${id}/documents`))
}

export function submitForVerification(id: string): Promise<VerificationStateDto> {
  return unwrap(api.post<VerificationStateDto>(`/businesses/${id}/verification-submissions`))
}

export function getVerification(id: string): Promise<VerificationStateDto> {
  return unwrap(api.get<VerificationStateDto>(`/businesses/${id}/verification`))
}

// ---- Products ----

export function searchProducts(params: ProductSearchParams = {}): Promise<PagedResult<ProductCardDto>> {
  return unwrap(api.get<PagedResult<ProductCardDto>>(`/products${query({ ...params })}`))
}

export function getProduct(idOrSlug: string): Promise<ProductDetailDto> {
  return unwrap(api.get<ProductDetailDto>(`/products/${idOrSlug}`))
}

export function createProduct(request: CreateProductRequest, businessId?: string): Promise<ProductDetailDto> {
  return unwrap(api.post<ProductDetailDto>(`/products${query({ business: businessId })}`, request))
}

export function updateProduct(id: string, request: UpdateProductRequest): Promise<ProductDetailDto> {
  return unwrap(api.put<ProductDetailDto>(`/products/${id}`, request))
}

export function deleteProduct(id: string): Promise<void> {
  return unwrap(api.delete(`/products/${id}`))
}

export function uploadProductImage(id: string, file: File, isPrimary = false): Promise<ProductImageDto> {
  const form = new FormData()
  form.append('file', file)
  if (isPrimary) form.append('isPrimary', 'true')
  return unwrap(api.post<ProductImageDto>(`/products/${id}/images`, form))
}

export function deleteProductImage(id: string, imageId: string): Promise<void> {
  return unwrap(api.delete(`/products/${id}/images/${imageId}`))
}

// ---- RFQs & quotations ----

export function createRfq(request: CreateTradeRequestRequest): Promise<TradeRequestDetailDto> {
  return unwrap(api.post<TradeRequestDetailDto>('/rfqs', request))
}

export function getOpenRfqs(params: {
  destinationCountry?: string
  categoryId?: string
  q?: string
  page?: number
  pageSize?: number
} = {}): Promise<PagedResult<TradeRequestCardDto>> {
  return unwrap(api.get<PagedResult<TradeRequestCardDto>>(`/rfqs/open${query({ ...params })}`))
}

export function getMyRfqs(params: RfqListParams = {}): Promise<PagedResult<TradeRequestCardDto>> {
  const { as, ...rest } = params
  return unwrap(api.get<PagedResult<TradeRequestCardDto>>(`/rfqs${query({ ...rest, as_: as })}`))
}

export function getRfq(id: string): Promise<TradeRequestDetailDto> {
  return unwrap(api.get<TradeRequestDetailDto>(`/rfqs/${id}`))
}

export function createQuotation(id: string, request: CreateQuotationRequest): Promise<QuotationDto> {
  return unwrap(api.post<QuotationDto>(`/rfqs/${id}/quotations`, request))
}

export function getQuotations(id: string): Promise<PagedResult<QuotationDto>> {
  return unwrap(api.get<PagedResult<QuotationDto>>(`/rfqs/${id}/quotations`))
}

export function acceptQuotation(id: string): Promise<QuotationDto> {
  return unwrap(api.post<QuotationDto>(`/quotations/${id}/accept`))
}

export function withdrawQuotation(id: string): Promise<QuotationDto> {
  return unwrap(api.post<QuotationDto>(`/quotations/${id}/withdraw`))
}

// ---- Messages ----

export function getConversations(): Promise<PagedResult<ConversationDto>> {
  return unwrap(api.get<PagedResult<ConversationDto>>('/messages/conversations'))
}

export function getThread(threadKey: string): Promise<PagedResult<MessageDto>> {
  return unwrap(api.get<PagedResult<MessageDto>>(`/messages${query({ threadKey })}`))
}

export function sendMessage(request: SendMessageRequest): Promise<MessageDto> {
  return unwrap(api.post<MessageDto>('/messages', request))
}

export function markThreadRead(threadKey: string): Promise<void> {
  return unwrap(api.put('/messages/read', { threadKey }))
}

// ---- Notifications ----

export function getNotifications(params: { unreadOnly?: boolean; page?: number; pageSize?: number } = {}): Promise<PagedResult<NotificationDto>> {
  return unwrap(api.get<PagedResult<NotificationDto>>(`/me/notifications${query({ ...params })}`))
}

export function getUnreadNotificationCount(): Promise<number> {
  return unwrap(api.get<number>('/me/notifications/unread-count'))
}

export function markNotificationRead(id: string): Promise<NotificationDto> {
  return unwrap(api.patch<NotificationDto>(`/me/notifications/${id}/read`))
}

export function markAllNotificationsRead(): Promise<void> {
  return unwrap(api.post('/me/notifications/read-all'))
}

// ---- Orders ----

/** Orders the signed-in user is buying. */
export function getMyOrders(params: OrderListParams = {}): Promise<PagedResult<OrderCardDto>> {
  return unwrap(api.get<PagedResult<OrderCardDto>>(`/orders${query({ ...params })}`))
}

/** Orders against businesses the signed-in user sells for. */
export function getSellingOrders(params: OrderListParams = {}): Promise<PagedResult<OrderCardDto>> {
  return unwrap(api.get<PagedResult<OrderCardDto>>(`/orders/selling${query({ ...params })}`))
}

export function getOrder(id: string): Promise<OrderDetailDto> {
  return unwrap(api.get<OrderDetailDto>(`/orders/${id}`))
}

/**
 * Drives an order to its next state. The server decides whether the transition is legal and
 * whether this user is allowed to make it; `to` is an OrderStatus value.
 */
export function transitionOrder(id: string, request: OrderTransitionRequest): Promise<OrderDetailDto> {
  return unwrap(api.post<OrderDetailDto>(`/orders/${id}/transitions`, request))
}

// ---- Payments ----

/** Payments on an order. Visible to both parties. */
export function getOrderPayments(orderId: string): Promise<PaymentDto[]> {
  return unwrap(api.get<PaymentDto[]>(`/orders/${orderId}/payments`))
}

export function getPayment(id: string): Promise<PaymentDto> {
  return unwrap(api.get<PaymentDto>(`/payments/${id}`))
}

/**
 * The order's ledger: one immutable row per money movement, oldest first. Gateway and
 * off-platform payments both land here, so it is the one financial record for the order.
 */
export function getOrderTransactions(orderId: string): Promise<TransactionDto[]> {
  return unwrap(api.get<TransactionDto[]>(`/orders/${orderId}/transactions`))
}

/**
 * Records a bank transfer or letter of credit the seller confirms has landed. Seller only — the
 * buyer sends the money, but only the beneficiary of the account can see it arrive.
 *
 * The remittance reference is unique per order, so a duplicate is refused server-side regardless
 * of idempotency keys. Pass a stable `idempotencyKey` to make a retried request safe as well.
 */
export function recordManualPayment(orderId: string, request: RecordManualPaymentRequest): Promise<PaymentDto> {
  return unwrap(api.post<PaymentDto>(`/orders/${orderId}/payments/manual`, request))
}

/**
 * Raises a gateway charge against an order. Buyer only, and refused on a closed order.
 *
 * Pass a stable `idempotencyKey` and a retry is safe: the same key returns the original payment
 * rather than raising a second charge. Generate one per attempt, keep it across retries.
 */
export function initiatePayment(orderId: string, request: InitiatePaymentRequest = {}): Promise<PaymentDto> {
  return unwrap(api.post<PaymentDto>(`/orders/${orderId}/payments`, request))
}

/**
 * Sandbox checkout: drives a charge to settled or failed by dispatching a real signed webhook
 * through the production handler. Scoped under /sandbox so it is unmistakably a dev affordance.
 */
export function settleSandboxPayment(id: string, outcome: 'succeeded' | 'failed'): Promise<PaymentDto> {
  return unwrap(api.post<PaymentDto>(`/sandbox/payments/${id}/settle`, { outcome }))
}

// ---- Trade documents ----

/**
 * Documents on an order. Both parties see every document on the order they share, so there is
 * no per-document visibility to filter on here.
 */
export function getOrderDocuments(orderId: string): Promise<TradeDocumentDto[]> {
  return unwrap(api.get<TradeDocumentDto[]>(`/orders/${orderId}/documents`))
}

/**
 * Attach a document to an order. Either party may upload; the server refuses a cancelled or
 * completed order, and rejects an expiry date earlier than the issue date.
 */
export function uploadOrderDocument(
  orderId: string,
  request: UploadTradeDocumentRequest,
  file: File,
): Promise<TradeDocumentDto> {
  const form = new FormData()
  form.append('file', file)
  form.append('docType', String(request.docType))
  if (request.documentReference) form.append('documentReference', request.documentReference)
  if (request.issuedOnUtc) form.append('issuedOnUtc', request.issuedOnUtc)
  if (request.expiresOnUtc) form.append('expiresOnUtc', request.expiresOnUtc)
  return unwrap(api.post<TradeDocumentDto>(`/orders/${orderId}/documents`, form))
}

/**
 * Withdraw a document. Permitted for the uploader (and an admin) while it is unverified; a
 * verified document is 400 DOCUMENT_ALREADY_VERIFIED and stays on the record.
 */
export function deleteOrderDocument(documentId: string): Promise<void> {
  return unwrap(api.delete(`/documents/${documentId}`))
}

// ---- Shipments ----

/**
 * The order's consignments. Visible to both parties. At most one today; a list so that splitting
 * a consignment later is not a breaking change for anything holding a parsed object.
 */
export function getOrderShipments(orderId: string): Promise<ShipmentDto[]> {
  return unwrap(api.get<ShipmentDto[]>(`/orders/${orderId}/shipments`))
}

/**
 * Records the consignment for an order. Seller only, and refused on a draft or cancelled order.
 *
 * Ports and incoterm default to the order's own snapshotted trade terms, so the common case needs
 * no route input at all. The first milestone is written here too — a shipment never exists with
 * nothing said about it — which is why the booking can be dated.
 */
export function createShipment(orderId: string, request: CreateShipmentRequest): Promise<ShipmentDto> {
  return unwrap(api.post<ShipmentDto>(`/orders/${orderId}/shipments`, request))
}

export function getShipment(id: string): Promise<ShipmentDto> {
  return unwrap(api.get<ShipmentDto>(`/shipments/${id}`))
}

/** Every consignment the caller is party to, most recently moved first. */
export function getShipments(params: { as?: 'buyer' | 'seller'; status?: number } = {}): Promise<ShipmentDto[]> {
  const filters: Record<string, string | number | undefined> = {}
  if (params.as) filters.as = params.as
  if (params.status !== undefined) filters.status = params.status
  return unwrap(api.get<ShipmentDto[]>(`/shipments${query(filters)}`))
}

/**
 * Appends a tracking milestone. Seller only, and permanent: milestones are append-only, so a
 * mistake is corrected by adding the right one rather than editing the wrong one.
 *
 * The milestone becomes the shipment's status, because that is the only definition of it. It
 * cannot be dated in the future.
 */
export function appendTrackingEvent(
  shipmentId: string,
  request: AppendTrackingEventRequest,
): Promise<ShipmentTrackingEventDto> {
  return unwrap(api.post<ShipmentTrackingEventDto>(`/shipments/${shipmentId}/tracking-events`, request))
}

/**
 * Issues a fresh guest tracking token, killing the previous link, and returns the raw value.
 * Seller only.
 *
 * This is the only place the raw token is ever visible. The server stores just its hash and has no
 * endpoint that can recover it, so if it is lost here it must be rotated. Handle the response
 * immediately - show it, let the seller copy it - and do not expect to read it back.
 */
export function rotateTrackingToken(shipmentId: string): Promise<TrackingTokenDto> {
  return unwrap(api.post<TrackingTokenDto>(`/shipments/${shipmentId}/tracking-token`, {}))
}

/**
 * Public consignment tracking. Needs no account - that is the point, since the link goes to freight
 * forwarders and customs agents who have none - so it is fetched with a bare axios call rather
 * than the authenticated client. Attaching a token that may be stale or absent would be noise, and
 * the response interceptor's 401 refresh path has no business anywhere near an anonymous endpoint.
 *
 * Goes through `unwrap` so a caller gets the same `problemFrom` shape as every other call; a bad
 * or rotated token arrives as `TRACKING_NOT_FOUND`.
 */
export function trackShipment(token: string): Promise<ShipmentPublicTrackingDto> {
  return unwrap(
    axios.get<ShipmentPublicTrackingDto>(`/api/v1/shipments/track/${encodeURIComponent(token)}`),
  )
}

// ---- Admin ----

export function adminSearchBusinesses(params: { status?: number; q?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<BusinessSummaryDto>> {
  return unwrap(api.get<PagedResult<BusinessSummaryDto>>(`/admin/businesses${query({ ...params })}`))
}

export function reviewBusinessVerification(id: string, request: ReviewVerificationRequest): Promise<BusinessDetailDto> {
  return unwrap(api.post<BusinessDetailDto>(`/admin/businesses/${id}/verification/review`, request))
}

/** The admin trade-document queue. Unset filters mean "any". */
export function adminSearchDocuments(params: DocumentSearchParams = {}): Promise<PagedResult<TradeDocumentDto>> {
  return unwrap(api.get<PagedResult<TradeDocumentDto>>(`/admin/documents${query({ ...params })}`))
}

/**
 * Verify or reject a document. Notes are required by the server when rejecting: a rejection
 * the uploader cannot act on is not a usable outcome.
 */
export function reviewDocument(id: string, request: ReviewDocumentRequest): Promise<TradeDocumentDto> {
  return unwrap(api.post<TradeDocumentDto>(`/admin/documents/${id}/review`, request))
}