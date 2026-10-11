import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { ClockIcon } from '../components/icons'
import { adminExportAuditLog, adminSearchAuditLog } from '../lib/trade'
import { ExportButton } from '../components/ExportButton'
import { problemFrom } from '../lib/api'
import { formatUtc } from '../lib/format'
import { AUDIT_ACTIONS } from '../types/trade'
import type { AdminAuditLogDto, PagedResult } from '../types/trade'

/**
 * Short verb for an action, for the badge.
 *
 * <p>
 * Derived from the trailing segment of the dotted action rather than mapped from a lookup table,
 * so an action the server gains later still reads as something rather than "Unknown". The full
 * dotted name stays in the row for anyone reading the detail.
 * </p>
 */
function verbOf(action: string): string {
  const tail = action.split('.').at(-1) ?? action
  return tail.charAt(0).toUpperCase() + tail.slice(1)
}

/** Which thing was acted on, worded for a person rather than for a schema. */
function entityLabel(entry: AdminAuditLogDto): string {
  switch (entry.entityType) {
    case 'Business':
      return 'Supplier business'
    case 'TradeDocument':
      return 'Trade document'
    default:
      return entry.entityType
  }
}

/** Rejections read differently from approvals, and the reason is the point of the row. */
function toneOf(action: string): string {
  if (action.endsWith('.rejected')) {
    return 'bg-red-50 text-red-700 ring-red-200'
  }
  if (action.endsWith('.approved') || action.endsWith('.verified')) {
    return 'bg-brand-50 text-brand-700 ring-brand-200'
  }
  return 'bg-stone-100 text-stone-600 ring-stone-200'
}

export default function AdminAuditLogPage() {
  const [action, setAction] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminAuditLogDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchAuditLog({
          action: action || undefined,
          q: term.trim() || undefined,
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [action, term, page])

  useEffect(() => {
    void load()
  }, [load])

  // Any filter change restarts at page one, or a narrower result set would show an empty page 4
  // with matching rows still waiting back on page 1.
  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-5xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Audit trail"
          subtitle="Every approval and rejection, with the reason that was given. Entries are written in the same transaction as the decision itself, so an action and the record of it cannot come apart."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search actions, summaries and reasons
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="A trading name, a rejection reason, a role"
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Action</span>
            <select
              value={action}
              onChange={(e) => applyFilter(setAction)(e.target.value)}
              className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            >
              <option value="">All actions</option>
              {AUDIT_ACTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          <ExportButton
            className="ml-auto"
            label="Export CSV"
            fileName={`audit-log-${new Date().toISOString().slice(0, 10)}.csv`}
            load={() =>
              adminExportAuditLog({
                action: action || undefined,
                q: term.trim() || undefined,
              })
            }
          />
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load the audit trail" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading the audit trail…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching actions"
              body="Approvals and rejections appear here as soon as they happen. The trail is append-only — nothing in it can be edited or removed."
              icon={<ClockIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {result!.total} entr{result!.total === 1 ? 'y' : 'ies'}, newest first
              </p>

              <ol className="space-y-2">
                {result!.items.map((entry) => (
                  <li key={entry.id}>
                    <Card className="px-4 py-3.5">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div className="min-w-0">
                          <p className="font-medium text-stone-800">{entry.summary}</p>
                          <p className="mt-0.5 text-xs text-stone-500">
                            <span className="font-medium text-stone-700">
                              {entry.actorDisplayName ?? 'Deleted account'}
                            </span>
                            {' · '}
                            {entityLabel(entry)}
                            {entry.entityId ? (
                              <>
                                {' · '}
                                <span className="font-mono text-[11px]">{entry.entityId}</span>
                              </>
                            ) : null}
                          </p>
                        </div>

                        <span
                          className={`shrink-0 rounded-md px-2 py-1 text-[11px] font-semibold ring-1 ${toneOf(entry.action)}`}
                          title={entry.action}
                        >
                          {verbOf(entry.action)}
                        </span>
                      </div>

                      {/*
                        The reason, in full and untruncated. It is the only part of the row that
                        tells an applicant what to fix or an auditor what was actually decided, so
                        clamping it would remove the row's reason for existing.
                      */}
                      {entry.details ? (
                        <p className="mt-2.5 rounded-lg bg-stone-50 px-3 py-2 text-sm text-stone-700">
                          {entry.details}
                        </p>
                      ) : null}

                      <p className="mt-2 text-[11px] text-stone-400">
                        {formatUtc(entry.occurredAtUtc)}
                      </p>
                    </Card>
                  </li>
                ))}
              </ol>

              {result!.totalPages > 1 ? (
                <div className="mt-4 flex items-center justify-between text-sm">
                  <button
                    type="button"
                    disabled={page <= 1}
                    onClick={() => setPage((p) => p - 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    ← Newer
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
                    Older →
                  </button>
                </div>
              ) : null}
            </>
          )}
        </div>

        <p className="mt-8 text-center text-xs text-stone-400">
          Timestamps are shown in UTC, which is what the server recorded.
        </p>
      </main>
    </div>
  )
}
