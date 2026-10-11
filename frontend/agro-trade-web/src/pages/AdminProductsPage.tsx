import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { ListingPill } from '../components/badges'
import { Modal } from '../components/overlays'
import { Button, Card, EmptyState, ErrorAlert, PageHeader, Spinner, TextArea } from '../components/ui'
import { TagIcon } from '../components/icons'
import { adminModerateProduct, adminSearchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc, money } from '../lib/format'
import {
  LISTING_STATUSES,
  MODERATION_STATUS,
  MODERATION_STATUSES,
  PRICE_UNITS,
  PRODUCT_MODERATION_ACTION,
  labelOf,
} from '../types/trade'
import type { AdminProductRowDto, PagedResult } from '../types/trade'

/** Seller states, plus "any". Index matches the server's `ListingStatus`. */
const LISTING_FILTERS = [
  { value: '', label: 'Any listing status' },
  ...LISTING_STATUSES.map((label, index) => ({ value: String(index), label })),
]

/**
 * The moderation filter leads with "Any" rather than defaulting to Suspended.
 *
 * A queue that opens pre-filtered to one state reads, on first load, as "there is nothing here"
 * for anyone who does not notice the filter. The queue's reason for existing is the *suspended*
 * rows, but an admin also needs to find an active listing to act on it.
 */
const MODERATION_FILTERS = [
  { value: '', label: 'Any moderation state' },
  ...MODERATION_STATUSES.map((label, index) => ({ value: String(index), label })),
]

const controlClass =
  'rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

export default function AdminProductsPage() {
  const [term, setTerm] = useState('')
  const [listingStatus, setListingStatus] = useState('')
  const [moderationStatus, setModerationStatus] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminProductRowDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)
  const [suspendTarget, setSuspendTarget] = useState<AdminProductRowDto | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchProducts({
          q: term.trim() || undefined,
          listingStatus: listingStatus === '' ? undefined : Number(listingStatus),
          moderationStatus: moderationStatus === '' ? undefined : Number(moderationStatus),
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [term, listingStatus, moderationStatus, page])

  useEffect(() => {
    void load()
  }, [load])

  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
    }
  }

  /**
   * Ends a suspension. Reinstating takes no reason — it is the resolution of a suspension, and the
   * suspension's own reason already says what was wrong.
   */
  async function reinstate(product: AdminProductRowDto) {
    setActionError(null)
    setBusyId(product.id)
    try {
      await adminModerateProduct(product.id, { action: PRODUCT_MODERATION_ACTION.Reinstate })
      await load()
    } catch (err) {
      setActionError(problemFrom(err).detail ?? 'The server refused the change.')
    } finally {
      setBusyId(null)
    }
  }

  function onModerated() {
    setSuspendTarget(null)
    void load()
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Products"
          subtitle="Every listing on the platform, with the seller's visibility choice and the platform's own moderation state side by side. Suspending a listing hides it from the marketplace without touching the seller's decision; reinstating ends the suspension and lets a listing that was Active return on its own."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search name, slug or HS code
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="Sesame seeds, a slug, 12074000"
              className={`w-full ${controlClass}`}
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Listing</span>
            <select
              value={listingStatus}
              onChange={(e) => applyFilter(setListingStatus)(e.target.value)}
              className={controlClass}
            >
              {LISTING_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Moderation</span>
            <select
              value={moderationStatus}
              onChange={(e) => applyFilter(setModerationStatus)(e.target.value)}
              className={controlClass}
            >
              {MODERATION_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {actionError ? (
          <div className="mt-4">
            <ErrorAlert title="Could not change the listing" detail={actionError} />
          </div>
        ) : null}

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load products" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading listings…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching listings"
              body="Try a different listing or moderation state, or clear the search."
              icon={<TagIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} listing{result!.total === 1 ? '' : 's'}, newest first
              </p>

              <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                {result!.items.map((product) => (
                  <li key={product.id} className="bg-white px-4 py-3.5">
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0">
                        {/*
                          Link only a listing a buyer could actually open. A draft or a suspended
                          listing has no public page, so linking it would send an admin to a
                          not-found instead of the row they were just reading.
                        */}
                        {product.isVisibleInMarketplace ? (
                          <Link
                            to={`/products/${product.slug}`}
                            className="font-medium text-brand-700 hover:underline"
                          >
                            {product.name}
                          </Link>
                        ) : (
                          <span className="font-medium text-stone-800">{product.name}</span>
                        )}
                        <p className="mt-0.5 truncate text-xs text-stone-500">
                          {product.sellerTradingName ?? 'Unknown seller'} · {product.originCountry}
                          {product.categoryName ? ` · ${product.categoryName}` : ''}
                        </p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        {product.moderationStatus === MODERATION_STATUS.Suspended ? (
                          <span className="rounded-md bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-700 ring-1 ring-red-200">
                            Suspended
                          </span>
                        ) : null}
                        <ListingPill status={product.listingStatus} />
                      </div>
                    </div>

                    <p className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-stone-500">
                      <span>
                        <span className="font-medium text-stone-700">
                          {money(product.price, product.currency)}
                        </span>{' '}
                        / {labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}
                      </span>
                      <span>Available {Number(product.availableQuantity).toLocaleString()}</span>
                      <span>{product.isVisibleInMarketplace ? 'Visible to buyers' : 'Not visible'}</span>
                      <span>{product.createdAtUtc ? `Created ${formatUtc(product.createdAtUtc)}` : 'Undated'}</span>
                    </p>

                    {product.moderationStatus === MODERATION_STATUS.Suspended && product.moderationReason ? (
                      <p className="mt-1 text-xs text-stone-500">
                        {/* The reason is the seller's to read, so it is shown to the admin verbatim. */}
                        Suspended: <span className="text-stone-600">{product.moderationReason}</span>
                      </p>
                    ) : null}

                    <div className="mt-3 flex justify-end">
                      {product.moderationStatus === MODERATION_STATUS.Suspended ? (
                        <button
                          type="button"
                          disabled={busyId === product.id}
                          onClick={() => void reinstate(product)}
                          className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-brand-700 ring-1 ring-brand-200 transition hover:bg-brand-50 disabled:opacity-50"
                        >
                          {busyId === product.id ? 'Reinstating…' : 'Reinstate'}
                        </button>
                      ) : (
                        <button
                          type="button"
                          onClick={() => {
                            setActionError(null)
                            setSuspendTarget(product)
                          }}
                          className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-red-700 ring-1 ring-red-200 transition hover:bg-red-50"
                        >
                          Suspend
                        </button>
                      )}
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
            Moderation and listing status are separate on purpose. Suspending withholds visibility
            without rewriting the seller's intent, and reinstating ends the suspension without
            quietly publishing a draft the seller never chose to put live.
          </p>
        </Card>
      </main>

      {suspendTarget ? (
        <SuspendListingDialog
          product={suspendTarget}
          onClose={() => setSuspendTarget(null)}
          onDone={onModerated}
        />
      ) : null}
    </div>
  )
}

/**
 * Suspends a listing.
 *
 * The reason is mandatory here and on the server: it is what the seller is shown, and it is the
 * only record that makes the action explicable later.
 */
function SuspendListingDialog({
  product,
  onClose,
  onDone,
}: {
  product: AdminProductRowDto
  onClose: () => void
  onDone: () => void
}) {
  const [reason, setReason] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    const trimmed = reason.trim()
    if (!trimmed) {
      setError('A suspension needs a reason. It is shown to the seller and recorded in the audit trail.')
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      await adminModerateProduct(product.id, {
        action: PRODUCT_MODERATION_ACTION.Suspend,
        reason: trimmed,
      })
      onDone()
    } catch (err) {
      setError(problemFrom(err).detail ?? 'The server refused the change.')
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Suspend a listing"
      description={product.name}
      size="md"
      dismissOnBackdrop={!submitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={submitting}>
            {submitting ? 'Suspending…' : 'Suspend listing'}
          </Button>
        </>
      }
    >
      <p className="text-sm text-stone-600">
        The listing is hidden from the marketplace immediately. The seller's own status is left
        untouched, so reinstating later restores exactly what they had chosen.
      </p>

      <div className="mt-4">
        <TextArea
          label="Reason"
          id="moderation-reason"
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Why is this listing being suspended?"
        />
      </div>

      {error ? (
        <div className="mt-4">
          <ErrorAlert title="Suspension refused" detail={error} />
        </div>
      ) : null}
    </Modal>
  )
}
