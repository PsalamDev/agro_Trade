// Wire-format mirrors of the Phase 4 logistics-provider directory + booking DTOs.
// Enums are ints on the wire; labels and bitmask helpers live here so pages stay declarative.

import type { PagedResult } from './trade'

export type { PagedResult }

// ---- Transport modes (mirrors AgroTrade.Domain.Enums.ShipmentMode) ----

export const SHIPMENT_MODES = ['Road', 'Rail', 'Sea', 'Air', 'Multimodal'] as const
export const SHIPMENT_MODE = { Road: 0, Rail: 1, Sea: 2, Air: 3, Multimodal: 4 } as const

export function modeLabel(value: number): string {
  return SHIPMENT_MODES[value] ?? String(value)
}

// ---- Services (bitmask over AgroTrade.Domain.Enums.LogisticsService) ----

export const LOGISTICS_SERVICE_OPTIONS = [
  { value: 1, label: 'Freight forwarding' },
  { value: 2, label: 'Customs brokerage' },
  { value: 4, label: 'Warehousing' },
  { value: 8, label: 'Trucking' },
  { value: 16, label: 'Insurance' },
  { value: 32, label: 'Inspection' },
  { value: 64, label: 'Cold chain' },
  { value: 128, label: 'Last mile' },
] as const

/** Friendly labels for a provider's service flags, in declaration order. */
export function serviceLabels(services: number[] | undefined): string[] {
  if (!services?.length) return []
  return LOGISTICS_SERVICE_OPTIONS.filter((option) => services.includes(option.value)).map((option) => option.label)
}

// ---- Booking status (mirrors AgroTrade.Domain.Enums.LogisticsBookingStatus) ----

export const LOGISTICS_BOOKING_STATUS = {
  Requested: 0,
  Accepted: 1,
  Declined: 2,
  Cancelled: 3,
  Completed: 4,
} as const

export const LOGISTICS_BOOKING_STATUS_LABELS = ['Requested', 'Accepted', 'Declined', 'Withdrawn', 'Completed'] as const

export function bookingStatusLabel(value: number): string {
  return LOGISTICS_BOOKING_STATUS_LABELS[value] ?? String(value)
}

/** Tailwind tone for a booking status pill, matching the platform's status colour language. */
export function bookingStatusTone(value: number): string {
  const tones: Record<number, string> = {
    0: 'bg-blue-100 text-blue-800',
    1: 'bg-emerald-100 text-emerald-800',
    2: 'bg-red-100 text-red-800',
    3: 'bg-stone-200 text-stone-600',
    4: 'bg-teal-100 text-teal-800',
  }
  return tones[value] ?? 'bg-stone-100 text-stone-700'
}

// ---- DTOs ----

export interface LogisticsProviderCardDto {
  id: string
  companyName: string
  description: string | null
  homeCountry: string
  modes: number[]
  services: number[]
  coverage: string[]
  isVerified: boolean
  rating: number
  ratingCount: number
}

export interface LogisticsProviderDetailDto extends LogisticsProviderCardDto {
  contactEmail: string | null
  contactPhone: string | null
  website: string | null
  isActive: boolean
  isMine: boolean
  createdAtUtc: string | null
  updatedAtUtc: string | null
}

export interface UpsertLogisticsProviderRequest {
  companyName: string
  description?: string | null
  homeCountry: string
  contactEmail?: string | null
  contactPhone?: string | null
  website?: string | null
  modes: number[]
  services: number[]
  coverage: string[]
  isActive: boolean
}

export interface LogisticsProviderQuery {
  q?: string
  country?: string
  mode?: number
  service?: number
  page?: number
  pageSize?: number
}

export interface LogisticsBookingDto {
  id: string
  reference: string
  orderId: string
  orderNumber: string
  orderDestinationCountry: string | null
  providerId: string
  providerName: string
  providerHomeCountry: string
  requestedByUserId: string
  requestedByDisplayName: string
  mode: number
  status: number
  notes: string | null
  responseNotes: string | null
  decidedAtUtc: string | null
  isRequester: boolean
  isProvider: boolean
  myRating: number | null
  createdAtUtc: string | null
}

export interface CreateLogisticsBookingRequest {
  providerId: string
  mode: number
  notes?: string | null
}

export interface RespondToLogisticsBookingRequest {
  accept: boolean
  notes?: string | null
}

// ---- Ratings ----

export interface LogisticsRatingDto {
  id: string
  bookingId: string
  providerId: string
  providerName: string
  ratedByUserId: string
  ratedByDisplayName: string
  rating: number
  comment: string | null
  isMine: boolean
  createdAtUtc: string | null
}

export interface CreateLogisticsRatingRequest {
  rating: number
  comment?: string | null
}
