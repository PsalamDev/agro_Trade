import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { RfqStatusPill } from '../components/badges'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { InboxIcon } from '../components/icons'
import { adminSearchRfqs } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc, money } from '../lib/format'
import { PRICE_UNITS, RFQ_STATUSES, labelOf } from '../types/trade'
import type { AdminRfqRowDto, PagedResult } from '../types/trade'

/** Every RFQ status, plus "any". Index matches the server's `RfqStatus`. */
const STATUS_FILTERS = [
  { value: '', label: 'Any status' },
  ...RFQ_STATUSES.map((label, index) => ({ value: String(index), label })),
]

/**
 * Unanswered is not a status — it is about whether any supplier has replied at all.
 *
 * Kept separate from the status filter because the two answer different questions: a status says
 * where the RFQ is in its own lifecycle, while "unanswered" says nobody has picked it up. An Open
 * RFQ with three quotations is not the same as an Open RFQ with none.
 */
const ANSWER_FILTERS = [
  { value: '', label: 'Answered or not' },
  { value: 'true', label: 'Unanswered' },
  { value: 'false', label: 'Has quotations' },
]

const controlClass =
  'rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

export default function AdminRfqsPage() {
  const [term, setTerm] = useState('')
  const [status, setStatus] = useState('')
  const [answered, setAnswered] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminRfqRowDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchRfqs({
          q: term.trim() || undefined,
          status: status === '' ? undefined : Number(status),
          unanswered: answered === '' ? undefined : answered === 'true',
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [term, status, answered, page])

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
          title="RFQs"
          subtitle="Every request for quotation on the platform, with what buyers asked for and how much of it suppliers have answered. This queue is read-only: an RFQ is the buyer's, and an administrator watches the board rather than editing it."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search title, number or destination
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="RFQ-2026-000001, sesame seeds, Apapa"
              className={`w-full ${controlClass}`}
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Status</span>
            <select
              value={status}
              onChange={(e) => applyFilter(setStatus)(e.target.value)}
              className={controlClass}
            >
              {STATUS_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Quotations</span>
            <select
              value={answered}
              onChange={(e) => applyFilter(setAnswered)(e.target.value)}
              className={controlClass}
            >
              {ANSWER_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load RFQs" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading RFQs…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching RFQs"
              body="Try a different status, widen the quotation filter, or clear the search."
              icon={<InboxIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} RFQ{result!.total === 1 ? '' : 's'}, newest first
              </p>

              <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                {result!.items.map((rfq) => (
                  <li key={rfq.id} className="bg-white px-4 py-3.5">
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0">
                        <Link
                          to={`/rfqs/${rfq.id}`}
                          className="font-medium text-brand-700 hover:underline"
                        >
                          {rfq.title}
                        </Link>
                        <p className="mt-0.5 truncate text-xs text-stone-500">
                          <span className="font-mono">{rfq.rfqNumber}</span> ·{' '}
                          {rfq.buyerDisplayName ?? 'Unknown buyer'}
                          {rfq.categoryName ? ` · ${rfq.categoryName}` : ''}
                        </p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        {rfq.quotationCount === 0 ? (
                          <span className="rounded-md bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-800 ring-1 ring-amber-200">
                            No offers
                          </span>
                        ) : null}
                        <RfqStatusPill status={rfq.status} />
                      </div>
                    </div>

                    <p className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-stone-500">
                      <span>
                        <span className="font-medium text-stone-700">
                          {Number(rfq.quantity).toLocaleString()}{' '}
                          {labelOf(PRICE_UNITS, rfq.unit).replace('Per ', '').toLowerCase()}
                        </span>
                        {rfq.budgetMax !== null
                          ? ` · up to ${money(rfq.budgetMax, rfq.preferredCurrency)}`
                          : ''}
                      </span>
                      <span>
                        To {rfq.destinationPortOrCity ?? '—'}
                        {rfq.destinationCountry ? `, ${rfq.destinationCountry}` : ''}
                      </span>
                      <span>
                        {count(rfq.quotationCount)} quotation
                        {rfq.quotationCount === 1 ? '' : 's'}
                      </span>
                      <span>{rfq.createdAtUtc ? `Filed ${formatUtc(rfq.createdAtUtc)}` : 'Undated'}</span>
                      <span>Expires {formatUtc(rfq.expiresAtUtc)}</span>
                    </p>
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
            A quotation count is a property of the RFQ, not of any one supplier: one eager seller
            quoting everything cannot make the board read as covered, because every RFQ they answered
            still counts as answered only for itself.
          </p>
        </Card>
      </main>
    </div>
  )
}
