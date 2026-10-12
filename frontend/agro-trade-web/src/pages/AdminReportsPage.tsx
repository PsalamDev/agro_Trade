import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner, Stat } from '../components/ui'
import { ChartIcon } from '../components/icons'
import { adminReport } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtcDate, money } from '../lib/format'
import { REPORT_KINDS } from '../types/trade'
import type { AdminReportDto, ReportKind } from '../types/trade'

const controlClass =
  'rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

/**
 * A `type="date"` value is a plain calendar day with no zone. The window the server filters on is
 * inclusive and its rows are stamped in UTC, so the day is pinned to its UTC bounds: midnight at
 * the start, the last instant before midnight at the end. Sending the bare day would silently drop
 * everything that happened after 00:00 on the closing date.
 */
function windowBound(value: string, edge: 'start' | 'end'): string | undefined {
  if (!value) {
    return undefined
  }
  return edge === 'start' ? `${value}T00:00:00Z` : `${value}T23:59:59.999Z`
}

const subtitleOf: Record<string, string> = Object.fromEntries(
  REPORT_KINDS.map((kind) => [kind.value, kind.body]),
)

export default function AdminReportsPage() {
  const [kind, setKind] = useState<ReportKind>('corridors')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [report, setReport] = useState<AdminReportDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setReport(
        await adminReport(kind, {
          from: windowBound(from, 'start'),
          to: windowBound(to, 'end'),
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [kind, from, to])

  useEffect(() => {
    void load()
  }, [load])

  const windowLabel =
    from || to
      ? `${from ? formatUtcDate(windowBound(from, 'start')) : 'the beginning'} → ${
          to ? formatUtcDate(windowBound(to, 'end')) : 'now'
        }`
      : 'All time'

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Reports"
          subtitle="Four views of the same platform, each over an optional date window. A window is inclusive on both ends; leaving both dates empty runs the report over all time."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap gap-2">
          {REPORT_KINDS.map((option) => {
            const active = option.value === kind
            return (
              <button
                key={option.value}
                type="button"
                title={option.body}
                aria-pressed={active}
                onClick={() => setKind(option.value)}
                className={`inline-flex items-center rounded-lg px-3 py-1.5 text-xs font-medium transition ${
                  active
                    ? 'bg-brand-600 text-white'
                    : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50'
                }`}
              >
                {option.label}
              </button>
            )
          })}
        </div>

        <div className="mt-5 flex flex-wrap items-end gap-3">
          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">From</span>
            <input
              type="date"
              value={from}
              max={to || undefined}
              onChange={(e) => setFrom(e.target.value)}
              className={controlClass}
            />
          </label>
          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">To</span>
            <input
              type="date"
              value={to}
              min={from || undefined}
              onChange={(e) => setTo(e.target.value)}
              className={controlClass}
            />
          </label>
          {from || to ? (
            <button
              type="button"
              onClick={() => {
                setFrom('')
                setTo('')
              }}
              className="rounded-lg bg-white px-3 py-2 text-xs font-medium text-stone-600 ring-1 ring-stone-200 transition hover:bg-stone-50"
            >
              Clear window
            </button>
          ) : null}

          <p className="ml-auto text-xs text-stone-500">
            {subtitleOf[kind] ?? ''} · {windowLabel}
          </p>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not run the report" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {loading && report === null ? (
            <Spinner label="Running the report…" />
          ) : report === null ? null : (
            <ReportBody report={report} />
          )}
        </div>
      </main>
    </div>
  )
}

function ReportBody({ report }: { report: AdminReportDto }) {
  switch (report.kind) {
    case 'corridors':
      return <Corridors report={report} />
    case 'rfq':
      return <Rfq report={report} />
    case 'verification-funnel':
      return <Funnel report={report} />
    case 'commodities':
      return <Commodities report={report} />
    default:
      return null
  }
}

/** A shared shell so every report reads as the same kind of object with a different body. */
function Panel({ children, note }: { children: ReactNode; note?: string }) {
  return (
    <Card className="p-0">
      {children}
      {note ? (
        <p className="border-t border-stone-100 px-4 py-3 text-xs text-stone-500">{note}</p>
      ) : null}
    </Card>
  )
}

function Corridors({ report }: { report: AdminReportDto }) {
  if (report.corridors.length === 0) {
    return (
      <EmptyState
        title="No trade in this window"
        body="No order was placed, and no money moved, between these dates."
        icon={<ChartIcon className="h-5 w-5" />}
      />
    )
  }

  return (
    <Panel note="Gross is what was ordered; net is what the ledger says actually arrived. There is no FX rate in the platform, so rows are shown per currency and never summed across them.">
      <ul className="divide-y divide-stone-100">
        {report.corridors.map((row, index) => (
          <li key={`${row.originPort}-${row.destinationCountry}-${row.currency}-${index}`} className="px-4 py-3.5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="text-sm font-medium text-stone-800">
                  {row.originPort || 'Unrecorded port'} → {row.destinationCountry || 'Unrecorded destination'}
                </p>
                <p className="mt-0.5 text-xs text-stone-500">
                  {count(row.orderCount)} order{row.orderCount === 1 ? '' : 's'} · {row.currency}
                  {row.mismatchedLedgerEntries > 0
                    ? ` · ${count(row.mismatchedLedgerEntries)} currency mismatch`
                    : ''}
                </p>
              </div>
              <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs">
                <div className="text-right">
                  <dt className="text-stone-500">Ordered</dt>
                  <dd className="font-medium text-stone-800">{money(row.grossOrderValue, row.currency)}</dd>
                </div>
                <div className="text-right">
                  <dt className="text-stone-500">Settled</dt>
                  <dd className="font-medium text-stone-800">{money(row.netSettled, row.currency)}</dd>
                </div>
                <div className="text-right">
                  <dt className="text-stone-500">Outstanding</dt>
                  <dd className={`font-medium ${row.outstanding > 0 ? 'text-amber-700' : 'text-stone-800'}`}>
                    {money(row.outstanding, row.currency)}
                  </dd>
                </div>
              </dl>
            </div>
          </li>
        ))}
      </ul>
    </Panel>
  )
}

function Rfq({ report }: { report: AdminReportDto }) {
  const rfq = report.rfq
  if (!rfq) {
    return null
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Open RFQs" value={count(rfq.demand.openRfqs)} />
        <Stat label="Total RFQs" value={count(rfq.demand.totalRfqs)} tone="neutral" />
        <Stat
          label="Unanswered open"
          value={count(rfq.demand.unansweredOpenRfqs)}
          tone={rfq.demand.unansweredOpenRfqs > 0 ? 'amber' : 'neutral'}
        />
        <Stat
          label="Average ceiling"
          value={
            rfq.demand.averageBudgetCeiling === null
              ? '—'
              : money(rfq.demand.averageBudgetCeiling, rfq.demand.preferredCurrency)
          }
          tone="neutral"
        />
      </div>

      <Panel note="Every status is listed, including empty ones: a funnel that hides its zeros cannot show a drop.">
        <ul className="divide-y divide-stone-100">
          {rfq.byStatus.map((stage) => (
            <li key={stage.status} className="flex items-center justify-between px-4 py-3">
              <span className="text-sm text-stone-700">{stage.label}</span>
              <span className="font-display text-sm font-bold text-stone-900">{count(stage.count)}</span>
            </li>
          ))}
        </ul>
      </Panel>
    </div>
  )
}

function Funnel({ report }: { report: AdminReportDto }) {
  const max = Math.max(1, ...report.stages.map((stage) => stage.count))
  return (
    <Panel note="Where supplier registrations sit in the verification pipeline, over the chosen window by registration date.">
      <ul className="divide-y divide-stone-100">
        {report.stages.map((stage) => (
          <li key={stage.status} className="px-4 py-3">
            <div className="flex items-center justify-between text-sm">
              <span className="text-stone-700">{stage.label}</span>
              <span className="font-display font-bold text-stone-900">{count(stage.count)}</span>
            </div>
            <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-stone-100">
              <div
                className="h-full rounded-full bg-brand-500"
                style={{ width: `${Math.round((stage.count / max) * 100)}%` }}
              />
            </div>
          </li>
        ))}
      </ul>
    </Panel>
  )
}

function Commodities({ report }: { report: AdminReportDto }) {
  if (report.commodities.length === 0) {
    return (
      <EmptyState
        title="No commodities yet"
        body="No category has been listed or asked for."
        icon={<ChartIcon className="h-5 w-5" />}
      />
    )
  }

  return (
    <Panel note="Published listings are supply a buyer can act on; RFQ counts are interest. A category with demand and no published supply is the gap worth reading.">
      <ul className="divide-y divide-stone-100">
        {report.commodities.map((row) => (
          <li key={row.categoryName} className="flex flex-wrap items-start justify-between gap-3 px-4 py-3.5">
            <div>
              <p className="text-sm font-medium text-stone-800">{row.categoryName}</p>
              <p className="mt-0.5 text-xs text-stone-500">
                {count(row.publishedCount)} published of {count(row.productCount)} listing
                {row.productCount === 1 ? '' : 's'}
              </p>
            </div>
            <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs">
              <div className="text-right">
                <dt className="text-stone-500">RFQs</dt>
                <dd className="font-medium text-stone-800">{count(row.rfqCount)}</dd>
              </div>
              <div className="text-right">
                <dt className="text-stone-500">Open</dt>
                <dd className={`font-medium ${row.openRfqCount > 0 ? 'text-amber-700' : 'text-stone-800'}`}>
                  {count(row.openRfqCount)}
                </dd>
              </div>
            </dl>
          </li>
        ))}
      </ul>
    </Panel>
  )
}
