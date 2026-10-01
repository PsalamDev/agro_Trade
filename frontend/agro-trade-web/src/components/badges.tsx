import {
  INCOTERMS,
  LISTING_STATUSES,
  ORDER_STATUSES,
  PAYMENT_STATUSES,
  QUOTATION_STATUSES,
  RFQ_STATUSES,
  VERIFICATION_STATUSES,
  labelOf,
} from '../types/trade'

export function VerifiedBadge({ visible, size = 'sm' }: { visible: boolean; size?: 'sm' | 'lg' }) {
  if (!visible) return null
  const pad = size === 'lg' ? 'px-2.5 py-1 text-xs' : 'px-1.5 py-0.5 text-[10px]'
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full bg-emerald-100 font-semibold uppercase tracking-wide text-emerald-800 ${pad}`}
      title="Verified business"
    >
      <svg viewBox="0 0 20 20" fill="currentColor" className="h-3 w-3" aria-hidden="true">
        <path
          fillRule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
          clipRule="evenodd"
        />
      </svg>
      Verified
    </span>
  )
}

const verificationTone = (value: number) => {
  const tones = ['bg-stone-100 text-stone-700', 'bg-amber-100 text-amber-800', 'bg-emerald-100 text-emerald-800', 'bg-red-100 text-red-800', 'bg-stone-200 text-stone-700']
  return tones[value] ?? tones[0]
}

export function VerificationPill({ status }: { status: number }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${verificationTone(status)}`}>
      {labelOf(VERIFICATION_STATUSES, status)}
    </span>
  )
}

export function ListingPill({ status }: { status: number }) {
  const tones: Record<number, string> = {
    0: 'bg-stone-100 text-stone-700',
    1: 'bg-emerald-100 text-emerald-800',
    2: 'bg-stone-200 text-stone-600',
  }
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones[status] ?? 'bg-stone-100 text-stone-700'}`}>
      {labelOf(LISTING_STATUSES, status)}
    </span>
  )
}

export function RfqStatusPill({ status }: { status: number }) {
  const tones: Record<number, string> = {
    0: 'bg-emerald-100 text-emerald-800',
    1: 'bg-emerald-50 text-emerald-700',
    2: 'bg-blue-100 text-blue-800',
    3: 'bg-indigo-100 text-indigo-800',
    4: 'bg-indigo-100 text-indigo-800',
    5: 'bg-stone-200 text-stone-600',
    6: 'bg-stone-100 text-stone-700',
    7: 'bg-red-100 text-red-800',
  }
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones[status] ?? 'bg-stone-100 text-stone-700'}`}>
      {labelOf(RFQ_STATUSES, status)}
    </span>
  )
}

export function QuotationPill({ status }: { status: number }) {
  const tones: Record<number, string> = {
    0: 'bg-stone-100 text-stone-700',
    1: 'bg-amber-100 text-amber-800',
    2: 'bg-blue-100 text-blue-800',
    3: 'bg-emerald-100 text-emerald-800',
    4: 'bg-red-100 text-red-800',
    5: 'bg-stone-200 text-stone-600',
    6: 'bg-stone-200 text-stone-600',
  }
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones[status] ?? 'bg-stone-100 text-stone-700'}`}>
      {labelOf(QUOTATION_STATUSES, status)}
    </span>
  )
}

export function OrderStatusPill({ status }: { status: number }) {
  const tones: Record<number, string> = {
    0: 'bg-stone-100 text-stone-700',
    1: 'bg-blue-100 text-blue-800',
    2: 'bg-cyan-100 text-cyan-800',
    3: 'bg-amber-100 text-amber-800',
    4: 'bg-teal-100 text-teal-800',
    5: 'bg-emerald-100 text-emerald-800',
    6: 'bg-red-100 text-red-800',
  }
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones[status] ?? 'bg-stone-100 text-stone-700'}`}>
      {labelOf(ORDER_STATUSES, status)}
    </span>
  )
}

export function OrderPaymentPill({ status }: { status: number }) {
  const tones: Record<number, string> = {
    0: 'bg-stone-100 text-stone-600',
    1: 'bg-amber-100 text-amber-800',
    2: 'bg-amber-100 text-amber-800',
    3: 'bg-emerald-100 text-emerald-800',
    4: 'bg-stone-100 text-stone-600',
    5: 'bg-red-100 text-red-800',
  }
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones[status] ?? 'bg-stone-100 text-stone-600'}`}>
      {labelOf(PAYMENT_STATUSES, status)}
    </span>
  )
}

export function IncotermBadge({ value }: { value: number | null | undefined }) {
  if (value === null || value === undefined) return null
  return (
    <span className="rounded-md bg-stone-100 px-1.5 py-0.5 font-mono text-xs font-semibold text-stone-700">
      {INCOTERMS[value] ?? value}
    </span>
  )
}

/** Wraps content in a white card with border + shadow, matching dashboard tiles. */