import { api, problemFrom } from './api'
import type {
  BusinessDetailDto,
  BusinessDocumentDto,
  BusinessSearchParams,
  BusinessSummaryDto,
  CategoryDto,
  ConversationDto,
  CreateProductRequest,
  CreateQuotationRequest,
  CreateTradeRequestRequest,
  HsCodeEntryDto,
  MessageDto,
  NotificationDto,
  PagedResult,
  ProductCardDto,
  ProductDetailDto,
  ProductImageDto,
  ProductSearchParams,
  QuotationDto,
  RegisterBusinessRequest,
  ReviewVerificationRequest,
  RfqListParams,
  SendMessageRequest,
  TradeCountriesRequest,
  TradeRequestCardDto,
  TradeRequestDetailDto,
  UpdateBusinessRequest,
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

// ---- Admin ----

export function adminSearchBusinesses(params: { status?: number; q?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<BusinessSummaryDto>> {
  return unwrap(api.get<PagedResult<BusinessSummaryDto>>(`/admin/businesses${query({ ...params })}`))
}

export function reviewBusinessVerification(id: string, request: ReviewVerificationRequest): Promise<BusinessDetailDto> {
  return unwrap(api.post<BusinessDetailDto>(`/admin/businesses/${id}/verification/review`, request))
}