import axios from 'axios'
import { api, problemFrom } from './api'
import type {
  CreateLogisticsBookingRequest,
  CreateLogisticsRatingRequest,
  LogisticsBookingDto,
  LogisticsProviderCardDto,
  LogisticsProviderDetailDto,
  LogisticsProviderQuery,
  LogisticsRatingDto,
  PagedResult,
  RespondToLogisticsBookingRequest,
  UpsertLogisticsProviderRequest,
} from '../types/logistics'

/** Await this and it throws problemFrom(...) on failure. */
async function unwrap<T>(request: Promise<{ data: T }>): Promise<T> {
  try {
    const { data } = await request
    return data
  } catch (error) {
    throw problemFrom(error)
  }
}

function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const qs = search.toString()
  return qs ? `?${qs}` : ''
}

// ---- Directory ----

export function searchLogisticsProviders(
  params: LogisticsProviderQuery = {},
): Promise<PagedResult<LogisticsProviderCardDto>> {
  return unwrap(
    api.get<PagedResult<LogisticsProviderCardDto>>(
      `/logistics/providers${query({
        q: params.q,
        country: params.country,
        mode: params.mode,
        service: params.service,
        page: params.page,
        pageSize: params.pageSize,
      })}`,
    ),
  )
}

export function getLogisticsProvider(id: string): Promise<LogisticsProviderDetailDto> {
  return unwrap(api.get<LogisticsProviderDetailDto>(`/logistics/providers/${id}`))
}

/**
 * The caller's own provider profile, or null when they have not created one yet. A 404 here is a
 * normal answer — it is how the profile page knows to open the form in create mode.
 */
export async function getMyLogisticsProvider(): Promise<LogisticsProviderDetailDto | null> {
  try {
    const { data } = await api.get<LogisticsProviderDetailDto>('/logistics/providers/mine')
    return data
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.status === 404) return null
    throw problemFrom(error)
  }
}

export function upsertMyLogisticsProvider(
  request: UpsertLogisticsProviderRequest,
): Promise<LogisticsProviderDetailDto> {
  return unwrap(api.put<LogisticsProviderDetailDto>('/logistics/providers/mine', request))
}

// ---- Bookings ----

export function requestLogisticsBooking(
  orderId: string,
  request: CreateLogisticsBookingRequest,
): Promise<LogisticsBookingDto> {
  return unwrap(api.post<LogisticsBookingDto>(`/orders/${orderId}/logistics-bookings`, request))
}

export function getOrderLogisticsBookings(orderId: string): Promise<LogisticsBookingDto[]> {
  return unwrap(api.get<LogisticsBookingDto[]>(`/orders/${orderId}/logistics-bookings`))
}

export function getMyLogisticsBookings(
  params: { status?: number; page?: number; pageSize?: number } = {},
): Promise<PagedResult<LogisticsBookingDto>> {
  return unwrap(api.get<PagedResult<LogisticsBookingDto>>(`/logistics/bookings${query(params)}`))
}

export function respondToLogisticsBooking(
  id: string,
  request: RespondToLogisticsBookingRequest,
): Promise<LogisticsBookingDto> {
  return unwrap(api.post<LogisticsBookingDto>(`/logistics/bookings/${id}/respond`, request))
}

export function cancelLogisticsBooking(id: string): Promise<LogisticsBookingDto> {
  return unwrap(api.post<LogisticsBookingDto>(`/logistics/bookings/${id}/cancel`, {}))
}

export function completeLogisticsBooking(id: string): Promise<LogisticsBookingDto> {
  return unwrap(api.post<LogisticsBookingDto>(`/logistics/bookings/${id}/complete`, {}))
}

// ---- Ratings ----

export function rateLogisticsBooking(
  id: string,
  request: CreateLogisticsRatingRequest,
): Promise<LogisticsRatingDto> {
  return unwrap(api.post<LogisticsRatingDto>(`/logistics/bookings/${id}/rating`, request))
}

export function getLogisticsProviderRatings(
  id: string,
  params: { page?: number; pageSize?: number } = {},
): Promise<PagedResult<LogisticsRatingDto>> {
  return unwrap(api.get<PagedResult<LogisticsRatingDto>>(`/logistics/providers/${id}/ratings${query(params)}`))
}
