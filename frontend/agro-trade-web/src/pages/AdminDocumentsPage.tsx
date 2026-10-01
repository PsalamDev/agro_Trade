import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { DocumentStatusPill } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { adminSearchDocuments, reviewDocument } from '../lib/trade'
import { problemFrom } from '../lib/api'
import {
  DOCUMENT_STATUS,
  TRADE_DOCUMENT_TYPE_OPTIONS,
  tradeDocumentLabel,
} from '../types/trade'
import type { TradeDocumentDto } from '../types/trade'

/**
 * Filters mirror the states the workflow can actually reach. Draft is unreachable — an upload
 * goes straight into the queue — and Under review and Expired are reserved values the server
 * does not yet write, so offering them would just be dead filters.
 */
const STATUS_FILTERS = [
  { value: String(DOCUMENT_STATUS.Uploaded), label: 'Awaiting review' },
  { value: String(DOCUMENT_STATUS.Verified), label: 'Verified' },
  { value: String(DOCUMENT_STATUS.Rejected), label: 'Rejected' },
  { value: '', label: 'All' },
]

function formatDateTime(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString()
}

export default function AdminDocumentsPage() {
  const [status, setStatus] = useState(STATUS_FILTERS[0].value)
  const [docType, setDocType] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)

  const [result, setResult] = useState<{ items: TradeDocumentDto[]; total: number; totalPages: number } | null>(
    null,
  )
  const [selected, setSelected] = useState<TradeDocumentDto | null>(null)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [actionError, setActionError] = useState<{ code?: string; detail?: string } | null>(null)
  const [pending, setPending] = useState(false)

  const load = useCallback(async () => {
    setError(null)
    try {
      const data = await adminSearchDocuments({
        status: status === '' ? undefined : Number(status),
        docType: docType === '' ? undefined : Number(docType),
        q: term.trim() || undefined,
        page,
        pageSize: 25,
      })
      setResult(data)
      // Keep the open detail in step with the row it came from, so a review that changes the
      // status also moves the row out of a filtered list rather than leaving a stale card.
      setSelected((current) =>
        current ? (data.items.find((d) => d.id === current.id) ?? null) : null,
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [status, docType, term, page])

  useEffect(() => {
    void load()
  }, [load])

  // Any filter change starts the list again from the top, or a narrower result set would show
  // an empty page 4 with matching rows still waiting on page 1.
  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
      setSelected(null)
      setNotes('')
      setActionError(null)
    }
  }

  async function handleReview(verdict: 'verify' | 'reject') {
    if (!selected) return
    setPending(true)
    setActionError(null)
    try {
      await reviewDocument(selected.id, {
        status: verdict === 'verify' ? DOCUMENT_STATUS.Verified : DOCUMENT_STATUS.Rejected,
        notes: notes.trim() || undefined,
      })
      setNotes('')
      await load()
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  const waitingCount = useMemo(() => result?.items.filter((d) => d.status === DOCUMENT_STATUS.Uploaded).length ?? 0, [result])

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Document review</h1>
            <p className="mt-1 text-sm text-stone-500">
              Trade paperwork uploaded against orders. Nothing counts as valid until it is
              verified here.
            </p>
          </div>
          <Link
            to="/admin/verification"
            className="text-sm font-medium text-brand-700 hover:underline"
          >
            Business verification →
          </Link>
        </div>

        <div className="mt-6 flex flex-wrap gap-2">
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

        <div className="mt-3 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-xs">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search file name, reference or order number
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="INV-2026 or AG-2026-000001"
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            />
          </label>
          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Document type</span>
            <select
              value={docType}
              onChange={(e) => applyFilter(setDocType)(e.target.value)}
              className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            >
              <option value="">Any type</option>
              {TRADE_DOCUMENT_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load the queue" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6 grid gap-6 lg:grid-cols-5">
          <div className="lg:col-span-3">
            {result === null ? (
              <Spinner label="Loading documents…" />
            ) : result.items.length === 0 ? (
              <EmptyState
                title="No documents in this state"
                body="Uploads and reviews change this list."
              />
            ) : (
              <>
                <p className="mb-2 text-xs text-stone-500">
                  {result.total} document{result.total === 1 ? '' : 's'}
                  {waitingCount > 0 ? ` · ${waitingCount} awaiting review on this page` : ''}
                </p>
                <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                  {result.items.map((doc) => (
                    <li key={doc.id}>
                      <button
                        type="button"
                        onClick={() => {
                          setSelected(doc)
                          setNotes('')
                          setActionError(null)
                        }}
                        className={`w-full px-4 py-3 text-left transition ${
                          selected?.id === doc.id ? 'bg-brand-50/60' : 'bg-white hover:bg-stone-50'
                        }`}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div className="min-w-0">
                            <p className="truncate font-medium">{tradeDocumentLabel(doc.docType)}</p>
                            <p className="truncate text-sm text-stone-500">
                              {doc.fileName ?? 'No file name'}
                              {doc.documentReference ? ` · ${doc.documentReference}` : ''}
                            </p>
                          </div>
                          <DocumentStatusPill status={doc.status} />
                        </div>
                        <p className="mt-1 text-xs text-stone-400">
                          <span className="font-mono">{doc.orderNumber || '—'}</span>
                          {doc.orderCounterparty ? ` · ${doc.orderCounterparty}` : ''}
                          {doc.uploadedByDisplayName ? ` · by ${doc.uploadedByDisplayName}` : ''}
                          {' · '}
                          {formatDateTime(doc.createdAtUtc)}
                        </p>
                      </button>
                    </li>
                  ))}
                </ul>

                {result.totalPages > 1 ? (
                  <div className="mt-3 flex items-center justify-between text-sm">
                    <button
                      type="button"
                      disabled={page <= 1}
                      onClick={() => setPage((p) => p - 1)}
                      className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                    >
                      ← Previous
                    </button>
                    <span className="text-xs text-stone-500">
                      Page {page} of {result.totalPages}
                    </span>
                    <button
                      type="button"
                      disabled={page >= result.totalPages}
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

          <div className="lg:col-span-2">
            {selected ? (
              <Card className="sticky top-6">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <h2 className="text-lg font-semibold">{tradeDocumentLabel(selected.docType)}</h2>
                    <p className="mt-0.5 truncate text-sm text-stone-500">
                      {selected.fileName ?? 'No file name'}
                    </p>
                  </div>
                  <DocumentStatusPill status={selected.status} />
                </div>

                <dl className="mt-4 space-y-2 rounded-xl bg-stone-50 p-4 text-sm">
                  <div className="flex justify-between gap-3">
                    <dt className="text-stone-500">Order</dt>
                    <dd className="font-mono">{selected.orderNumber || '—'}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-stone-500">Counterparty</dt>
                    <dd className="font-medium">{selected.orderCounterparty ?? '—'}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-stone-500">Uploaded by</dt>
                    <dd className="font-medium">{selected.uploadedByDisplayName ?? '—'}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-stone-500">Uploaded</dt>
                    <dd>{formatDateTime(selected.createdAtUtc)}</dd>
                  </div>
                  {selected.documentReference ? (
                    <div className="flex justify-between gap-3">
                      <dt className="text-stone-500">Reference</dt>
                      <dd className="font-mono">{selected.documentReference}</dd>
                    </div>
                  ) : null}
                  {selected.issuedOnUtc ? (
                    <div className="flex justify-between gap-3">
                      <dt className="text-stone-500">Issued</dt>
                      <dd>{formatDateTime(selected.issuedOnUtc)}</dd>
                    </div>
                  ) : null}
                  {selected.expiresOnUtc ? (
                    <div className="flex justify-between gap-3">
                      <dt className="text-stone-500">Expires</dt>
                      <dd>{formatDateTime(selected.expiresOnUtc)}</dd>
                    </div>
                  ) : null}
                </dl>

                {selected.fileUrl ? (
                  <a
                    href={selected.fileUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="mt-4 block rounded-lg bg-stone-100 px-4 py-2.5 text-center text-sm font-semibold text-stone-700 transition hover:bg-stone-200"
                  >
                    Open the file
                  </a>
                ) : (
                  <p className="mt-4 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
                    This document has no stored file, so there is nothing to inspect.
                  </p>
                )}

                {selected.reviewNotes ? (
                  <p className="mt-4 rounded-lg bg-stone-50 px-3 py-2 text-sm text-stone-600">
                    <span className="font-semibold">Previous notes:</span> {selected.reviewNotes}
                  </p>
                ) : null}

                {selected.status === DOCUMENT_STATUS.Uploaded ? (
                  <div className="mt-5 border-t border-stone-100 pt-4">
                    <h3 className="text-sm font-semibold text-stone-700">Review</h3>
                    {actionError ? (
                      <div className="mt-3">
                        <ErrorAlert
                          detail={
                            actionError.code === 'REVIEW_NOTES_REQUIRED'
                              ? 'Rejecting a document requires a note explaining why.'
                              : actionError.detail
                          }
                        />
                      </div>
                    ) : null}
                    <label className="mt-3 block">
                      <span className="mb-1.5 block text-xs font-medium text-stone-700">
                        Notes — required to reject, sent to the uploader
                      </span>
                      <textarea
                        value={notes}
                        onChange={(e) => setNotes(e.target.value)}
                        rows={3}
                        maxLength={2000}
                        placeholder="What is wrong with this document, or what was checked?"
                        className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                      />
                    </label>
                    <div className="mt-3 flex gap-2">
                      <button
                        type="button"
                        disabled={pending}
                        onClick={() => void handleReview('verify')}
                        className="flex-1 rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:opacity-60"
                      >
                        {pending ? 'Reviewing…' : 'Verify'}
                      </button>
                      <button
                        type="button"
                        disabled={pending || notes.trim() === ''}
                        title={notes.trim() === '' ? 'A rejection needs a reason' : undefined}
                        onClick={() => void handleReview('reject')}
                        className="flex-1 rounded-lg border border-red-200 bg-red-50 px-4 py-2 text-sm font-semibold text-red-700 transition hover:bg-red-100 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        Reject
                      </button>
                    </div>
                  </div>
                ) : (
                  <p className="mt-5 border-t border-stone-100 pt-4 text-sm text-stone-500">
                    Reviewed on {formatDateTime(selected.reviewedAtUtc)}. Reversing a decision is a
                    re-review, which the server allows only to a different verdict.
                  </p>
                )}
              </Card>
            ) : (
              <div className="grid h-full place-items-center rounded-xl border border-dashed border-stone-300 bg-stone-50 p-12 text-center">
                <p className="font-medium text-stone-700">Select a document to review</p>
                <p className="mt-1 text-sm text-stone-500">
                  Its file, order context, and the review buttons appear here.
                </p>
              </div>
            )}
          </div>
        </div>
      </main>
    </div>
  )
}
