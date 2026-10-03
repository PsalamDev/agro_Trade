import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ShipmentStatusPill } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { trackShipment } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { INCOTERMS, SHIPMENT_MODES, SHIPMENT_STATUSES, labelOf } from '../types/trade'
import type { ShipmentPublicTrackingDto } from '../types/trade'

/**
 * Formatted as UTC, deliberately. The API's timestamps carry no offset — SQL Server's `datetime2`
 * has none to carry — so the wire value is a UTC wall-clock reading and treating it as local would
 * shift every milestone by the reader's offset. On a page whose whole purpose is to say *when* a
 * container moved, that is the one error worth engineering against.
 */
function formatDateTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
  })
}

function formatDate(value: string | null): string | null {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  return date.toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

function route(shipment: ShipmentPublicTrackingDto): string {
  const from = [shipment.originPort, shipment.originCountry].filter(Boolean).join(', ')
  const to = [shipment.destinationPort, shipment.destinationCountry].filter(Boolean).join(', ')
  if (!from && !to) return 'Route not recorded'
  return `${from || 'Origin not recorded'} → ${to || 'Destination not recorded'}`
}

/**
 * Anonymous consignment tracking.
 *
 * Reached from a link the seller hands out — to the buyer, to a freight forwarder, to a customs
 * agent. Those people have no account here and will not make one, so the page cannot sit behind
 * authentication. What it shows is correspondingly narrow: the route, the carrier, the ETA and the
 * milestones, and nothing else. No order number, no counterparty, no goods, no prices. A forwarded
 * link is a real risk in this trade, and the token is the only thing standing between the holder
 * and a shipment's whereabouts — so the payload behind it is deliberately not worth stealing.
 *
 * The server answers 404 for an unknown or rotated token rather than 401 or 403, so this page
 * cannot be used to tell a valid consignment from an invalid one.
 */
export default function TrackShipmentPage() {
  const { token } = useParams()
  const [shipment, setShipment] = useState<ShipmentPublicTrackingDto | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!token) {
      setLoading(false)
      return
    }
    let cancelled = false
    setLoading(true)
    setError(null)
    trackShipment(token)
      .then((result) => {
        if (!cancelled) setShipment(result)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [token])

  return (
    <div className="min-h-screen bg-stone-50">
      <AppHeader />
      <main className="mx-auto max-w-3xl px-4 py-10">
        <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">
          Consignment tracking
        </p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight text-stone-900">
          Where the goods are
        </h1>
        <p className="mt-1 text-sm text-stone-500">
          {shipment ? (
            <>
              Reference <span className="font-mono">{shipment.reference}</span>
            </>
          ) : (
            'Entered from a link shared by the seller.'
          )}
        </p>

        {loading ? (
          <div className="mt-6">
            <Spinner label="Loading the consignment." />
          </div>
        ) : error ? (
          <div className="mt-6">
            <EmptyState
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={1.5}
                  className="h-5 w-5"
                  aria-hidden="true"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m0 3.75h.008v.008H12v-.008z" />
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
              }
              title="This tracking link does not work"
              body={
                error.code === 'TRACKING_NOT_FOUND'
                  ? 'The seller may have replaced the link, or the consignment may never have had one issued. Ask them for a current link — links cannot be looked up, so there is nothing to recover here.'
                  : error.detail
              }
              action={
                <Link to="/" className="text-sm font-medium text-brand-700 hover:underline">
                  Go to the marketplace
                </Link>
              }
            />
          </div>
        ) : shipment ? (
          <>
            <Card className="mt-6">
              <div className="flex flex-wrap items-center gap-2">
                <ShipmentStatusPill status={shipment.status} />
                <span className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-700">
                  {labelOf(SHIPMENT_MODES, shipment.mode)}
                </span>
                {shipment.incoterms !== null ? (
                  <span className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-700">
                    {labelOf(INCOTERMS, shipment.incoterms)}
                  </span>
                ) : null}
              </div>

              <p className="mt-3 text-lg font-medium text-stone-900">{route(shipment)}</p>

              <dl className="mt-4 grid gap-4 sm:grid-cols-2">
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">
                    Carrier
                  </dt>
                  <dd className="mt-0.5 text-sm text-stone-800">{shipment.carrierName}</dd>
                  {shipment.trackingReference ? (
                    <dd className="mt-0.5 font-mono text-xs text-stone-500">
                      {shipment.trackingReference}
                    </dd>
                  ) : null}
                </div>
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">
                    Timing
                  </dt>
                  <dd className="mt-0.5 text-sm text-stone-800">
                    {formatDate(shipment.departedAtUtc)
                      ? `Departed ${formatDate(shipment.departedAtUtc)}`
                      : 'Not yet departed'}
                  </dd>
                  <dd className="mt-0.5 text-sm text-stone-800">
                    {formatDate(shipment.estimatedArrivalUtc)
                      ? `Estimated arrival ${formatDate(shipment.estimatedArrivalUtc)}`
                      : 'No arrival estimate yet'}
                  </dd>
                </div>
              </dl>
            </Card>

            <Card className="mt-4">
              <h2 className="text-base font-semibold text-stone-900">Journey</h2>
              {shipment.events.length === 0 ? (
                <p className="mt-3 text-sm text-stone-500">
                  Nothing has been reported for this consignment yet.
                </p>
              ) : (
                <ol className="mt-4">
                  {shipment.events.map((event, index) => (
                    <li key={`${event.occurredAtUtc}-${index}`} className="flex gap-3">
                      <div className="flex flex-col items-center">
                        <span
                          className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${
                            index === shipment.events.length - 1 ? 'bg-brand-600' : 'bg-stone-300'
                          }`}
                        />
                        {index < shipment.events.length - 1 ? (
                          <span className="w-px flex-1 bg-stone-200" />
                        ) : null}
                      </div>
                      <div className="min-w-0 flex-1 pb-5">
                        <div className="flex flex-wrap items-baseline gap-x-2">
                          <span className="text-sm font-medium text-stone-800">
                            {labelOf(SHIPMENT_STATUSES, event.status)}
                          </span>
                          <span className="text-xs text-stone-500">
                            {formatDateTime(event.occurredAtUtc)} UTC
                          </span>
                        </div>
                        {event.location ? (
                          <p className="mt-0.5 text-xs text-stone-600">{event.location}</p>
                        ) : null}
                        {event.description ? (
                          <p className="mt-0.5 text-xs text-stone-500">{event.description}</p>
                        ) : null}
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </Card>

            <p className="mt-4 text-xs text-stone-400">
              Milestones are reported by the seller as the carrier updates them. All times are UTC.
              This page shows only the consignment — not the order, the parties, the goods or the
              price.
            </p>
          </>
        ) : null}

        {!loading && !error && !shipment ? (
          <div className="mt-6">
            <ErrorAlert detail="No tracking token was supplied." />
          </div>
        ) : null}
      </main>
    </div>
  )
}