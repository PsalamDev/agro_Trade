import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { ShipmentStatusPill } from '../components/badges'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { TruckIcon } from '../components/icons'
import { adminSearchShipments } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc } from '../lib/format'
import { SHIPMENT_MODES, SHIPMENT_STATUSES } from '../types/trade'
import type { AdminShipmentDto, PagedResult } from '../types/trade'

/**
 * Every shipment status, not just the ones in progress.
 *
 * <p>
 * Delivered and Exception are the two an administrator most often wants and least often think to
 * filter for. Arrived is included even though it is a brief state, because a consignment sitting
 * in Arrived is exactly the one where the next step is being missed.
 * </p>
 */
const STATUS_FILTERS = [
  { value: '', label: 'All' },
  ...SHIPMENT_STATUSES.map((label, index) => ({ value: String(index), label })),
]

const GUEST_FILTERS = [
  { value: '', label: 'Any' },
  { value: 'true', label: 'Guest link live' },
  { value: 'false', label: 'No guest link' },
]

export default function AdminShipmentsPage() {
  const [status, setStatus] = useState('')
  const [guest, setGuest] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminShipmentDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchShipments({
          q: term.trim() || undefined,
          // Status 0 (Pending) is a real value here, not a "no filter" sentinel, so it is passed
          // through whenever the button is selected rather than being treated as absent.
          status: status === '' ? undefined : Number(status),
          hasGuestTracking: guest === '' ? undefined : guest === 'true',
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [status, guest, term, page])

  useEffect(() => {
    void load()
  }, [load])

  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Consignments"
          subtitle="Tracking state for every shipment on the platform. A consignment's status is its most recent milestone — there is no separate status that could drift out of step with the record."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-xs">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search reference, carrier or order
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="SHP-2026-000001 or Maersk"
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Guest tracking</span>
            <select
              value={guest}
              onChange={(e) => applyFilter(setGuest)(e.target.value)}
              className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            >
              {GUEST_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          {STATUS_FILTERS.map((filter) => (
            <button
              key={filter.value}
              type="button"
              onClick={() => applyFilter(setStatus)(filter.value)}
              className={`rounded-lg px-3 py-1.5 text-xs font-medium transition ${
                status === filter.value
                  ? 'bg-brand-600 text-white'
                  : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50'
              }`}
            >
              {filter.label}
            </button>
          ))}
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load consignments" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading consignments…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching consignments"
              body="A consignment appears once a seller books one against an order."
              icon={<TruckIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} consignment{result!.total === 1 ? '' : 's'}
              </p>

              <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                {result!.items.map((shipment) => (
                  <li key={shipment.id} className="bg-white px-4 py-3.5">
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-mono text-sm font-semibold text-stone-800">
                          {shipment.reference}
                        </p>
                        <p className="mt-0.5 text-xs text-stone-500">
                          Order{' '}
                          <span className="font-mono">{shipment.orderNumber || '—'}</span>
                          {shipment.destinationCountry ? ` · ${shipment.destinationCountry}` : ''}
                        </p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <ShipmentStatusPill status={shipment.status} />
                        {/*
                          Worth surfacing rather than hiding in a column: a live guest link is a
                          bearer secret being handed to people with no account here. The link itself
                          cannot be read from here — only whether one exists.
                        */}
                        {shipment.hasGuestTracking ? (
                          <span className="rounded-md bg-amber-50 px-2 py-1 text-[11px] font-medium text-amber-800 ring-1 ring-amber-200">
                            Guest link live
                          </span>
                        ) : null}
                      </div>
                    </div>

                    <dl className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-xs text-stone-500">
                      <div className="flex gap-1.5">
                        <dt>Carrier</dt>
                        <dd className="font-medium text-stone-700">{shipment.carrierName}</dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Mode</dt>
                        <dd className="font-medium text-stone-700">
                          {SHIPMENT_MODES[shipment.mode] ?? '—'}
                        </dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Milestones</dt>
                        <dd className="font-medium text-stone-700">{count(shipment.milestoneCount)}</dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Last movement</dt>
                        <dd className="font-medium text-stone-700">
                          {formatUtc(shipment.statusSinceUtc)}
                        </dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Est. arrival</dt>
                        <dd className="font-medium text-stone-700">
                          {formatUtc(shipment.estimatedArrivalUtc)}
                        </dd>
                      </div>
                    </dl>
                  </li>
                ))}
              </ul>

              {result!.totalPages > 1 ? (
                <div className="mt-4 flex items-center justify-between text-sm">
                  <button
                    type="button"
                    disabled={page <= 1}
                    onClick={() => setPage((p) => p - 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    ← Previous
                  </button>
                  <span className="text-xs text-stone-500">
                    Page {page} of {result!.totalPages}
                  </span>
                  <button
                    type="button"
                    disabled={page >= result!.totalPages}
                    onClick={() => setPage((p) => p + 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    Next →
                  </button>
                </div>
              ) : null}
            </>
          )}
        </div>

        <Card className="mt-8 px-4 py-3.5">
          <p className="text-xs text-stone-500">
            This queue is read-only. Milestones are appended by the seller who booked the
            consignment, and nothing here can move one — an administrator cannot re-route a shipment
            that has already left the port.
          </p>
        </Card>
      </main>
    </div>
  )
}
