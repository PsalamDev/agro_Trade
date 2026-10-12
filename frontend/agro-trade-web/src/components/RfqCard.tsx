import { Link } from 'react-router-dom'
import type { TradeRequestCardDto } from '../types/trade'
import { PRICE_UNITS, SHIPMENT_MODES, labelOf } from '../types/trade'
import { IncotermBadge, RfqStatusPill } from './badges'
import { ArrowRightIcon, CoinsIcon, MapPinIcon, PackageIcon, TruckIcon } from './icons'
import { formatUtcDate } from '../lib/format'

export function RfqCard({ rfq }: { rfq: TradeRequestCardDto }) {
  const isOpen = rfq.status === 0 || rfq.status === 1
  const unit = labelOf(PRICE_UNITS, rfq.unit).replace('Per ', '').toLowerCase()

  return (
    <Link
      to={`/rfqs/${rfq.id}`}
      className="group flex flex-col rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
    >
      <div className="flex items-center justify-between gap-2">
        <p className="font-mono text-xs tracking-wide text-stone-400">{rfq.rfqNumber}</p>
        <RfqStatusPill status={rfq.status} />
      </div>
      <h3 className="font-display mt-2.5 leading-snug font-semibold transition group-hover:text-brand-700">
        {rfq.title}
      </h3>

      <p className="mt-3 flex items-center gap-1.5 text-sm text-stone-600">
        <MapPinIcon className="h-3.5 w-3.5 shrink-0 text-stone-400" />
        {rfq.destinationCountry}
        {rfq.destinationPortOrCity ? (
          <>
            <span className="text-stone-300">·</span>
            <span className="truncate">{rfq.destinationPortOrCity}</span>
          </>
        ) : null}
      </p>

      <dl className="mt-4 grid grid-cols-2 gap-2">
        <div className="rounded-xl bg-stone-50 px-2.5 py-2">
          <dt className="flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-stone-500 uppercase">
            <PackageIcon className="h-3.5 w-3.5" />
            Quantity
          </dt>
          <dd className="mt-0.5 text-sm font-semibold text-stone-800">
            {Number(rfq.quantity).toLocaleString()}{' '}
            <span className="font-normal text-stone-500">{unit}</span>
          </dd>
        </div>
        <div className="rounded-xl bg-stone-50 px-2.5 py-2">
          <dt className="flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-stone-500 uppercase">
            <TruckIcon className="h-3.5 w-3.5" />
            Transport
          </dt>
          <dd className="mt-0.5 text-sm font-semibold text-stone-800">
            {rfq.transportMode === null || rfq.transportMode === undefined
              ? '—'
              : labelOf(SHIPMENT_MODES, rfq.transportMode)}
          </dd>
        </div>
        <div className="col-span-2 rounded-xl bg-stone-50 px-2.5 py-2">
          <dt className="flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-stone-500 uppercase">
            <CoinsIcon className="h-3.5 w-3.5" />
            Preferred terms
          </dt>
          <dd className="mt-0.5 flex items-center gap-2 text-sm font-semibold text-stone-800">
            <span>{rfq.preferredCurrency}</span>
            {rfq.preferredIncoterm === null || rfq.preferredIncoterm === undefined ? (
              <span className="font-normal text-stone-500">No incoterm preference</span>
            ) : (
              <IncotermBadge value={rfq.preferredIncoterm} />
            )}
          </dd>
        </div>
      </dl>

      <div className="mt-auto flex items-center justify-between gap-3 border-t border-stone-100 pt-4 text-sm">
        <span className="text-stone-500">Expires {formatUtcDate(rfq.expiresAt)}</span>
        <span
          className={`inline-flex items-center gap-1 font-semibold transition duration-200 ease-soft group-hover:gap-2 ${
            isOpen ? 'text-brand-700' : 'text-stone-400'
          }`}
        >
          {isOpen
            ? rfq.quotationCount > 0
              ? `${rfq.quotationCount} quote${rfq.quotationCount > 1 ? 's' : ''}`
              : 'Open for quotes'
            : 'Closed'}
          <ArrowRightIcon className="h-3.5 w-3.5" />
        </span>
      </div>
    </Link>
  )
}
