import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Button, Card, ErrorAlert, TextArea } from './ui'
import { ConfirmDialog } from './overlays'
import { Stars, StarRatingInput } from './Stars'
import { formatUtc, money } from '../lib/format'
import { countryName } from '../lib/trade'
import {
  cancelLogisticsBooking,
  completeLogisticsBooking,
  rateLogisticsBooking,
  respondToLogisticsBooking,
} from '../lib/logistics'
import { problemFrom } from '../lib/api'
import {
  LOGISTICS_BOOKING_STATUS,
  bookingStatusLabel,
  bookingStatusTone,
  modeLabel,
  serviceLabels,
} from '../types/logistics'
import type { LogisticsBookingDto } from '../types/logistics'

type DialogKind = 'decline' | 'withdraw' | 'complete'

/**
 * One booking, with whatever actions the caller is entitled to. Shared between the standalone
 * bookings list and the panel on an order page, so the two can never disagree about who may do what.
 */
export function LogisticsBookingCard({
  booking,
  onChanged,
  showOrderLink = true,
}: {
  booking: LogisticsBookingDto
  onChanged: () => Promise<void> | void
  showOrderLink?: boolean
}) {
  const [dialog, setDialog] = useState<DialogKind | null>(null)
  const [notes, setNotes] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  // Rating is only ever about a completed booking, so it gets its own little form rather than being
  // folded into the action dialog, which is about changing the booking's state.
  const [ratingOpen, setRatingOpen] = useState(false)
  const [ratingValue, setRatingValue] = useState(0)
  const [ratingComment, setRatingComment] = useState('')

  const canRespond = booking.isProvider && booking.status === LOGISTICS_BOOKING_STATUS.Requested
  const canWithdraw = booking.isRequester && booking.status === LOGISTICS_BOOKING_STATUS.Requested
  const canComplete = booking.isRequester && booking.status === LOGISTICS_BOOKING_STATUS.Accepted

  async function run(action: () => Promise<unknown>) {
    setPending(true)
    setError(null)
    try {
      await action()
      setDialog(null)
      setNotes('')
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  async function submitRating() {
    setPending(true)
    setError(null)
    try {
      await rateLogisticsBooking(booking.id, {
        rating: ratingValue,
        comment: ratingComment.trim() || null,
      })
      setRatingOpen(false)
      setRatingValue(0)
      setRatingComment('')
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex items-center gap-2">
            <span className="font-mono text-sm font-semibold text-stone-900">{booking.reference}</span>
            {booking.isProvider ? (
              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-800">
                You are the provider
              </span>
            ) : null}
            {booking.isRequester ? (
              <span className="rounded-full bg-brand-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-brand-800">
                You requested
              </span>
            ) : null}
          </p>
          <p className="mt-1 text-sm text-stone-600">
            {showOrderLink ? (
              <Link to={`/dashboard/orders/${booking.orderId}`} className="font-medium text-brand-700 hover:underline">
                {booking.orderNumber}
              </Link>
            ) : (
              <span className="font-medium text-stone-700">{booking.orderNumber}</span>
            )}
            {booking.orderDestinationCountry ? ` · ${countryName(booking.orderDestinationCountry)}` : ''}
            {' · '}
            {booking.providerName} ({countryName(booking.providerHomeCountry)})
          </p>
        </div>
        <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${bookingStatusTone(booking.status)}`}>
          {bookingStatusLabel(booking.status)}
        </span>
      </div>

      <dl className="mt-4 grid gap-2 text-sm sm:grid-cols-2">
        <div className="flex justify-between gap-3">
          <dt className="text-stone-500">Mode</dt>
          <dd className="font-medium text-stone-800">{modeLabel(booking.mode)}</dd>
        </div>
        {booking.services.length ? (
          <div className="flex justify-between gap-3">
            <dt className="text-stone-500">Services</dt>
            <dd className="text-right font-medium text-stone-800">{serviceLabels(booking.services).join(', ')}</dd>
          </div>
        ) : null}
        {booking.insuredValue != null ? (
          <div className="flex justify-between gap-3">
            <dt className="text-stone-500">Insured value</dt>
            <dd className="font-medium text-stone-800">{money(booking.insuredValue, booking.currency)}</dd>
          </div>
        ) : null}
        <div className="flex justify-between gap-3">
          <dt className="text-stone-500">Requested</dt>
          <dd className="text-stone-700">{formatUtc(booking.createdAtUtc)}</dd>
        </div>
        {booking.decidedAtUtc ? (
          <div className="flex justify-between gap-3">
            <dt className="text-stone-500">Answered</dt>
            <dd className="text-stone-700">{formatUtc(booking.decidedAtUtc)}</dd>
          </div>
        ) : null}
      </dl>

      {booking.notes ? (
        <div className="mt-3 rounded-xl border border-stone-200 bg-stone-50 p-3 text-sm text-stone-700">
          <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Notes</p>
          <p className="mt-1 whitespace-pre-line">{booking.notes}</p>
        </div>
      ) : null}
      {booking.responseNotes ? (
        <div className="mt-2 rounded-xl border border-stone-200 bg-stone-50 p-3 text-sm text-stone-700">
          <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Provider reply</p>
          <p className="mt-1 whitespace-pre-line">{booking.responseNotes}</p>
        </div>
      ) : null}

      {error ? (
        <div className="mt-3">
          <ErrorAlert detail={error.detail} />
        </div>
      ) : null}

      {booking.isRequester && booking.status === LOGISTICS_BOOKING_STATUS.Completed ? (
        <div className="mt-4 border-t border-stone-100 pt-4">
          {booking.myRating ? (
            <div className="flex flex-wrap items-center gap-2 text-sm text-stone-600">
              <span className="font-medium text-stone-700">You rated this provider</span>
              <Stars value={booking.myRating} size="sm" />
            </div>
          ) : ratingOpen ? (
            <div>
              <p className="text-sm font-medium text-stone-700">Rate this provider</p>
              <div className="mt-2">
                <StarRatingInput value={ratingValue} onChange={setRatingValue} disabled={pending} />
              </div>
              <div className="mt-3">
                <TextArea
                  label="Comment (optional)"
                  id={`rating-${booking.id}`}
                  value={ratingComment}
                  rows={2}
                  onChange={(e) => setRatingComment(e.target.value)}
                />
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button size="sm" disabled={pending || ratingValue < 1} onClick={() => void submitRating()}>
                  Submit rating
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  disabled={pending}
                  onClick={() => {
                    setRatingOpen(false)
                    setRatingValue(0)
                    setRatingComment('')
                  }}
                >
                  Cancel
                </Button>
              </div>
            </div>
          ) : (
            <Button size="sm" variant="secondary" onClick={() => setRatingOpen(true)}>
              Rate this provider
            </Button>
          )}
        </div>
      ) : null}

      {canRespond || canWithdraw || canComplete ? (
        <div className="mt-4 flex flex-wrap gap-2 border-t border-stone-100 pt-4">
          {canRespond ? (
            <>
              <Button
                size="sm"
                disabled={pending}
                onClick={() => void run(() => respondToLogisticsBooking(booking.id, { accept: true }))}
              >
                Accept
              </Button>
              <Button size="sm" variant="secondary" disabled={pending} onClick={() => setDialog('decline')}>
                Decline
              </Button>
            </>
          ) : null}
          {canComplete ? (
            <Button size="sm" disabled={pending} onClick={() => setDialog('complete')}>
              Mark as completed
            </Button>
          ) : null}
          {canWithdraw ? (
            <Button size="sm" variant="danger" disabled={pending} onClick={() => setDialog('withdraw')}>
              Withdraw
            </Button>
          ) : null}
        </div>
      ) : null}

      <ConfirmDialog
        open={dialog === 'decline'}
        title="Decline this booking?"
        confirmLabel="Decline"
        pending={pending}
        onCancel={() => {
          setDialog(null)
          setNotes('')
        }}
        onConfirm={() => void run(() => respondToLogisticsBooking(booking.id, { accept: false, notes: notes.trim() || null }))}
        message={
          <>
            <p>Can you say why, so the requester is not left guessing?</p>
            <TextArea
              label="Reason (optional)"
              id={`decline-${booking.id}`}
              value={notes}
              rows={3}
              onChange={(e) => setNotes(e.target.value)}
            />
          </>
        }
      />
      <ConfirmDialog
        open={dialog === 'withdraw'}
        title="Withdraw this booking?"
        confirmLabel="Withdraw"
        pending={pending}
        onCancel={() => setDialog(null)}
        onConfirm={() => void run(() => cancelLogisticsBooking(booking.id))}
        message="The provider will no longer see this request. You can raise a new one any time."
      />
      <ConfirmDialog
        open={dialog === 'complete'}
        title="Mark this booking as completed?"
        confirmLabel="Mark completed"
        tone="primary"
        pending={pending}
        onCancel={() => setDialog(null)}
        onConfirm={() => void run(() => completeLogisticsBooking(booking.id))}
        message="Confirm the provider has delivered the service. This closes the booking."
      />
    </Card>
  )
}
