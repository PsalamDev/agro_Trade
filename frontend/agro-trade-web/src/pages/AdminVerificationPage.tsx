import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerificationPill, VerifiedBadge } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { adminSearchBusinesses, getBusiness, reviewBusinessVerification } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc } from '../lib/format'
import { DOCUMENT_TYPES, VERIFICATION_STATUS, labelOf } from '../types/trade'
import type { BusinessDetailDto, BusinessSummaryDto } from '../types/trade'

export default function AdminVerificationPage() {
  const [queue, setQueue] = useState<BusinessSummaryDto[] | null>(null)
  const [detail, setDetail] = useState<BusinessDetailDto | null>(null)
  const [statusFilter, setStatusFilter] = useState(String(VERIFICATION_STATUS.Pending))
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [actionError, setActionError] = useState<{ code?: string; detail?: string } | null>(null)
  const [pending, setPending] = useState(false)
  const [notes, setNotes] = useState('')

  const load = useCallback(async () => {
    setError(null)
    try {
      const result = await adminSearchBusinesses({ status: Number(statusFilter) || undefined, pageSize: 50 })
      setQueue(result.items)
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [statusFilter])

  useEffect(() => {
    void load()
  }, [load])

  async function openDetail(businessId: string) {
    setActionError(null)
    setNotes('')
    try {
      setDetail(await getBusiness(businessId))
    } catch (err) {
      setActionError(problemFrom(err))
    }
  }

  async function handleReview(approve: boolean) {
    if (!detail) return
    setPending(true)
    setActionError(null)
    try {
      const updated = await reviewBusinessVerification(detail.id, { approve, notes: notes || undefined })
      setDetail(updated)
      await load()
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Verification queue</h1>
            <p className="mt-1 text-sm text-stone-500">
              Review KYC documents and approve or reject business verification submissions.
            </p>
          </div>
        </div>

        <div className="mt-6 flex max-w-sm gap-2">
          {['1', '2', '3', '0'].map((value) => (
            <button
              key={value}
              type="button"
              onClick={() => setStatusFilter(value)}
              className={`rounded-lg px-4 py-2 text-sm font-medium transition ${
                statusFilter === value ? 'bg-brand-600 text-white' : 'bg-white text-stone-600 hover:bg-stone-100'
              }`}
            >
              {labelOf(['Unverified', 'Pending', 'Verified', 'Rejected'], Number(value))}
            </button>
          ))}
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load the queue" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6 grid gap-6 lg:grid-cols-5">
          <div className="lg:col-span-2">
            {queue === null ? (
              <Spinner />
            ) : queue.length === 0 ? (
              <EmptyState title="No businesses in this state" body="Uploads and reviews change this list." />
            ) : (
              <ul className="space-y-2">
                {queue.map((business) => (
                  <li key={business.id}>
                    <button
                      type="button"
                      onClick={() => void openDetail(business.id)}
                      className={`w-full rounded-xl border p-4 text-left transition ${
                        detail?.id === business.id
                          ? 'border-brand-300 bg-brand-50/50'
                          : 'border-stone-200 bg-white hover:border-brand-200'
                      }`}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <p className="truncate font-semibold">{business.tradingName}</p>
                          <p className="truncate text-sm text-stone-500">{business.legalName}</p>
                        </div>
                        <VerificationPill status={business.verificationStatus} />
                      </div>
                      <p className="mt-2 text-sm text-stone-500">
                        {labelOf(['Sole proprietor', 'Partnership', 'Limited company', 'Cooperative', 'NGO', 'Other'], business.businessType)}
                        {business.exportsTo.length > 0 ? ` · Exports: ${business.exportsTo.join(', ')}` : ''}
                      </p>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="lg:col-span-3">
            {detail ? (
              <Card>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="text-xl font-semibold">{detail.tradingName}</h2>
                    <p className="text-sm text-stone-500">{detail.legalName}</p>
                  </div>
                  <div className="flex flex-col items-end gap-1.5">
                    <VerificationPill status={detail.verificationStatus} />
                    <VerifiedBadge visible={detail.isVerifiedBadgeVisible} />
                  </div>
                </div>

                {detail.website ? (
                  <a href={detail.website} target="_blank" rel="noreferrer" className="mt-1 block break-all text-sm text-brand-700 hover:underline">
                    {detail.website}
                  </a>
                ) : null}
                {detail.description ? <p className="mt-3 text-sm leading-relaxed text-stone-600">{detail.description}</p> : null}

                {detail.verification?.requestedAtUtc ? (
                  <p className="mt-3 text-sm text-stone-500">
                    Submitted for review on {formatUtc(detail.verification.requestedAtUtc)}
                  </p>
                ) : null}
                {detail.verification?.notes ? (
                  <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">Previous notes: {detail.verification.notes}</p>
                ) : null}

                <h3 className="mt-6 text-sm font-semibold text-stone-700">KYC documents ({detail.documents.length})</h3>
                {detail.documents.length === 0 ? (
                  <p className="mt-2 text-sm text-stone-500">No documents uploaded.</p>
                ) : (
                  <ul className="mt-2 divide-y divide-stone-100 rounded-lg border border-stone-200">
                    {detail.documents.map((doc) => (
                      <li key={doc.id} className="flex items-center justify-between gap-3 px-3 py-2.5 text-sm">
                        <div className="min-w-0">
                          <p className="truncate font-medium">{labelOf(DOCUMENT_TYPES, doc.docType)}</p>
                          <p className="truncate text-xs text-stone-500">{doc.fileName}</p>
                        </div>
                        <a href={doc.fileUrl} target="_blank" rel="noreferrer" className="shrink-0 font-medium text-brand-700 hover:underline">
                          View document
                        </a>
                      </li>
                    ))}
                  </ul>
                )}

                {detail.verificationStatus === VERIFICATION_STATUS.Pending ? (
                  <div className="mt-6 border-t border-stone-100 pt-5">
                    {actionError ? (
                      <div className="mb-3">
                        <ErrorAlert detail={actionError.detail} />
                      </div>
                    ) : null}
                    <textarea
                      value={notes}
                      onChange={(e) => setNotes(e.target.value)}
                      placeholder="Review notes (shown to the business)…"
                      maxLength={2000}
                      className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                    />
                    <div className="mt-3 flex gap-2">
                      <button
                        type="button"
                        disabled={pending}
                        onClick={() => void handleReview(true)}
                        className="flex-1 rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:opacity-60"
                      >
                        {pending ? 'Reviewing…' : 'Approve'}
                      </button>
                      <button
                        type="button"
                        disabled={pending}
                        onClick={() => void handleReview(false)}
                        className="flex-1 rounded-lg border border-red-200 bg-red-50 px-4 py-2 text-sm font-semibold text-red-700 transition hover:bg-red-100 disabled:opacity-60"
                      >
                        Reject
                      </button>
                    </div>
                  </div>
                ) : (
                  <p className="mt-6 text-sm text-stone-500">
                    Reviewed on{' '}
                    {formatUtc(detail.verification?.reviewedAtUtc)}
                  </p>
                )}
              </Card>
            ) : queue !== null && queue.length > 0 ? (
              <div className="grid h-full place-items-center rounded-xl border border-dashed border-stone-300 bg-stone-50 p-12 text-center">
                <p className="font-medium text-stone-700">Select a business to review</p>
                <p className="mt-1 text-sm text-stone-500">Its profile, documents, and review buttons appear here.</p>
              </div>
            ) : (
              <div className="grid h-full place-items-center rounded-xl border border-dashed border-stone-300 bg-stone-50 p-12 text-center">
                <p className="font-medium text-stone-700">Nothing to review in this state</p>
                <p className="mt-1 text-sm text-stone-500">
                  <Link to="/admin/verification" className="text-brand-700 hover:underline">Pending</Link> is the default filter.
                </p>
              </div>
            )}
          </div>
        </div>
      </main>
    </div>
  )
}