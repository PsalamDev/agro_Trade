import { Link } from 'react-router-dom'
import type { TradeRequestCardDto } from '../types/trade'
import { PRICE_UNITS, SHIPMENT_MODES, labelOf } from '../types/trade'
import { INCOTERMS } from '../types/trade'
import { IncotermBadge, RfqStatusPill } from './badges'

export function RfqCard({ rfq }: { rfq: TradeRequestCardDto }) {
  const isOpen = rfq.status === 0 || rfq.status === 1
  return (
    <Link
      to={`/rfqs/${rfq.id}`}
      className="group flex flex-col rounded-xl border border-stone-200 bg-white p-5 shadow-sm transition hover:border-brand-300 hover:shadow"
    >
      <div className="flex items-start justify-between gap-2">
        <p className="font-mono text-xs text-stone-400">{rfq.rfqNumber}</p>
        <RfqStatusPill status={rfq.status} />
      </div>
      <h3 className="mt-1 font-semibold group-hover:text-brand-700">{rfq.title}</h3>

      <dl className="mt-4 grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
        <div>
          <dt className="text-xs text-stone-500">Destination</dt>
          <dd className="font-medium">
            {rfq.destinationCountry}
            {rfq.destinationPortOrCity ? ` · ${rfq.destinationPortOrCity}` : ''}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Quantity</dt>
          <dd className="font-medium">
            {Number(rfq.quantity).toLocaleString()} {labelOf(PRICE_UNITS, rfq.unit).replace('Per ', '').toLowerCase()}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Preferred incoterm</dt>
          <dd>
            <IncotermBadge value={rfq.preferredIncoterm} />
            {rfq.preferredIncoterm === null || rfq.preferredIncoterm === undefined ? '—' : null}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Transport</dt>
          <dd className="font-medium">
            {rfq.transportMode === null || rfq.transportMode === undefined
              ? '—'
              : labelOf(SHIPMENT_MODES, rfq.transportMode)}
          </dd>
        </div>
      </dl>

      <div className="mt-4 flex items-center justify-between border-t border-stone-100 pt-3 text-sm">
        <span className="text-stone-500">
          {rfq.preferredCurrency} · Expires {new Date(rfq.expiresAt).toLocaleDateString()}
        </span>
        {isOpen ? (
          <span className="font-medium text-brand-700">
            {rfq.quotationCount > 0 ? `${rfq.quotationCount} quote${rfq.quotationCount > 1 ? 's' : ''}` : 'Open for quotes'}
          </span>
        ) : (
          <span>{INCOTERMS[rfq.preferredIncoterm ?? -1] ?? ''}</span>
        )}
      </div>
    </Link>
  )
}