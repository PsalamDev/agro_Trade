import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { CoinsIcon } from '../components/icons'
import { adminSearchTransactions } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc, money } from '../lib/format'
import { TRANSACTION_TYPES } from '../types/trade'
import type { AdminTransactionRowDto, PagedResult } from '../types/trade'

/** Every ledger type, plus "any". Index matches the server's `TransactionType`. */
const TYPE_FILTERS = [
  { value: '', label: 'Every type' },
  ...TRANSACTION_TYPES.map((label, index) => ({ value: String(index), label })),
]

/**
 * The currencies the platform actually settles in.
 *
 * A short fixed list rather than free text: the ledger is per-currency and never converted, so a
 * typo'd filter would silently return nothing and read as "no such trade" rather than "no such
 * currency". A currency nobody trades in is not a useful thing to offer.
 */
const CURRENCY_FILTERS = ['', 'USD', 'NGN', 'KES', 'EUR', 'GBP']

const controlClass =
  'rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

/** A refund leaves the platform, so a negative entry is not a smaller payment — it is money out. */
function amountTone(amount: number): string {
  if (amount < 0) {
    return 'text-red-600'
  }
  return 'text-stone-900'
}

export default function AdminTransactionsPage() {
  const [term, setTerm] = useState('')
  const [type, setType] = useState('')
  const [currency, setCurrency] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminTransactionRowDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchTransactions({
          q: term.trim() || undefined,
          type: type === '' ? undefined : Number(type),
          currency: currency || undefined,
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [term, type, currency, page])

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
          title="Ledger"
          subtitle="Every movement of money on the platform, newest first. Rows are read from the ledger itself rather than rolled up from payments, so a corrected payment books a new entry instead of rewriting the old one."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search reference, bank trace, narrative or order
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="TXN-2026-000001, a UTR, an order number"
              className={`w-full ${controlClass}`}
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Type</span>
            <select
              value={type}
              onChange={(e) => applyFilter(setType)(e.target.value)}
              className={controlClass}
            >
              {TYPE_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Currency</span>
            <select
              value={currency}
              onChange={(e) => applyFilter(setCurrency)(e.target.value)}
              className={controlClass}
            >
              {CURRENCY_FILTERS.map((code) => (
                <option key={code || 'any'} value={code}>
                  {code || 'Any currency'}
                </option>
              ))}
            </select>
          </label>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load the ledger" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading ledger entries…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No ledger entries"
              body="Nothing matches these filters. Clear the search, or widen the type and currency."
              icon={<CoinsIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} entr{result!.total === 1 ? 'y' : 'ies'}, newest first
              </p>

              <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                {result!.items.map((row) => (
                  <li key={row.id} className="bg-white px-4 py-3.5">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="font-mono text-xs font-semibold text-stone-800">
                          {row.reference}
                        </p>
                        <p className="mt-0.5 truncate text-sm text-stone-600">
                          {row.narrative || TRANSACTION_TYPES[row.type] || 'Entry'}
                        </p>
                        <p className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-stone-500">
                          <span>
                            Order{' '}
                            <span className="font-medium text-stone-700">{row.orderNumber}</span>
                          </span>
                          <span>{formatUtc(row.occurredAtUtc)}</span>
                          {row.externalReference ? (
                            <span>
                              Ref <span className="font-mono">{row.externalReference}</span>
                            </span>
                          ) : null}
                        </p>
                      </div>

                      <div className="text-right">
                        <p className={`font-display text-sm font-bold ${amountTone(row.amount)}`}>
                          {money(row.amount, row.currency)}
                        </p>
                        {/*
                          The type is repeated beside the signed amount because the sign alone
                          does not say *why* money moved — a fee and a refund can both be small.
                        */}
                        <span className="text-xs text-stone-500">
                          {TRANSACTION_TYPES[row.type] ?? 'Entry'}
                        </span>
                      </div>
                    </div>
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
            The ledger is append-only. Refunds and fees are booked as signed entries rather than by
            editing the payment they relate to, so this page and a bank statement can be reconciled
            line by line long after the fact.
          </p>
        </Card>
      </main>
    </div>
  )
}
