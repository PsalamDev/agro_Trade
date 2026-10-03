import { useCallback, useEffect, useState } from 'react'
import { Button, ErrorAlert, Field, SelectField, Spinner, TextArea } from './ui'
import { IncotermBadge, ShipmentStatusPill } from './badges'
import { ConfirmDialog } from './overlays'
import {
  appendTrackingEvent,
  createShipment,
  getOrderShipments,
  rotateTrackingToken,
} from '../lib/trade'
import { problemFrom } from '../lib/api'
import {
  INCOTERMS,
  ORDER_STATUS,
  SHIPMENT_MODES,
  SHIPMENT_MODE,
  SHIPMENT_STATUSES,
  SHIPMENT_STATUS,
  labelOf,
} from '../types/trade'
import type { OrderDetailDto, ShipmentDto } from '../types/trade'

/**
 * Dates are formatted from the wire value as-is. The API's timestamps carry no offset, because
 * SQL Server's datetime2 has none to carry — so they are read as UTC wall-clock, which is what the
 * server meant. Formatting in the browser's zone instead would shift every milestone by the
 * reader's offset, on a page whose entire job is to say *when* something happened.
 */
function formatDateTime(value: string | null): string | null {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
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

/** A date input yields YYYY-MM-DD with no zone, so it is sent as UTC midnight rather than parsed. */
function utcMidnight(day: string): string {
  return `${day}T00:00:00Z`
}

function route(shipment: ShipmentDto): string {
  const from = [shipment.originPort, shipment.originCountry].filter(Boolean).join(', ')
  const to = [shipment.destinationPort, shipment.destinationCountry].filter(Boolean).join(', ')
  if (!from && !to) return 'Route not recorded'
  return `${from || 'Origin not recorded'} → ${to || 'Destination not recorded'}`
}

interface Props {
  order: OrderDetailDto
  onChanged: () => Promise<void> | void
}

/**
 * Consignment tracking for one order (task 2.6).
 *
 * The seller books a consignment against an order and then appends milestones as the goods move.
 * The buyer reads the same timeline. Nothing here can be edited or deleted — the timeline is the
 * evidence a buyer relies on if goods arrive late or short, so a correction is a further entry
 * rather than a rewrite, and every status shown is the latest dated milestone rather than a
 * separate field that could drift away from it.
 *
 * Booking is also the moment the first milestone is written and the guest link is issued. That
 * link is the only place a buyer or a freight forwarder can follow a consignment without an
 * account, and because the server stores just its hash the URL can never be read back — so it is
 * shown once, here, and the seller's only remedy for losing it is to rotate.
 *
 * The consignments are fetched here rather than folded into `OrderDetailDto`: the list endpoint
 * already exists, and carrying a whole timeline inside the order payload would make every order
 * read drag its own history along whether or not anyone looks at it.
 */
export function OrderShipmentsPanel({ order, onChanged }: Props) {
  const [shipments, setShipments] = useState<ShipmentDto[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<{ code?: string; detail?: string } | null>(null)
  const [booking, setBooking] = useState(false)

  const load = useCallback(async () => {
    try {
      setLoadError(null)
      setShipments(await getOrderShipments(order.id))
    } catch (err) {
      setLoadError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [order.id])

  useEffect(() => {
    void load()
  }, [load])

  const refresh = async () => {
    await load()
    await onChanged()
  }

  // A draft or cancelled order is closed to new consignments. A completed one is not: goods that
  // arrived short are still in dispute, and the timeline is the record of that.
  const bookable =
    order.isSeller &&
    order.status !== ORDER_STATUS.Draft &&
    order.status !== ORDER_STATUS.Cancelled

  return (
    <section className="mt-6 border-t border-stone-100 pt-6">
      <h2 className="text-lg font-semibold">Consignment</h2>
      <p className="mt-1 text-sm text-stone-500">
        Where the goods are. Both parties see the same milestones, in the order they happened.
      </p>

      {loadError ? (
        <div className="mt-3">
          <ErrorAlert detail={loadError.detail} />
        </div>
      ) : loading ? (
        <div className="mt-3">
          <Spinner label="Loading the consignment." />
        </div>
      ) : shipments.length === 0 ? (
        <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-6 text-center text-sm text-stone-500">
          No consignment recorded for this order yet.
        </p>
      ) : (
        <div className="mt-4 space-y-4">
          {shipments.map((shipment) => (
            <ShipmentCard key={shipment.id} shipment={shipment} onChanged={refresh} />
          ))}
        </div>
      )}

      {bookable ? (
        booking && shipments.length === 0 ? (
          <BookShipmentForm
            order={order}
            onCancel={() => setBooking(false)}
            onBooked={async () => {
              setBooking(false)
              await refresh()
            }}
          />
        ) : shipments.length === 0 && !loading ? (
          <div className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
            <h3 className="text-sm font-semibold text-stone-700">Handed the goods to a carrier?</h3>
            <p className="mt-1 text-xs text-stone-500">
              Record the consignment so the buyer can follow it. Ports and terms default to this
              order’s own, so there is usually nothing to type.
            </p>
            <div className="mt-3">
              <Button size="sm" onClick={() => setBooking(true)}>
                Record a consignment
              </Button>
            </div>
          </div>
        ) : null
      ) : null}
    </section>
  )
}

/** One consignment: its facts, its guest link, and its timeline. */
function ShipmentCard({
  shipment,
  onChanged,
}: {
  shipment: ShipmentDto
  onChanged: () => Promise<void> | void
}) {
  const [adding, setAdding] = useState(false)
  const [rotating, setRotating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [issued, setIssued] = useState<{ token: string; trackingPath: string } | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  async function handleRotate() {
    setBusy(true)
    setRotating(true)
    setError(null)
    setCopied(false)
    try {
      const result = await rotateTrackingToken(shipment.id)
      setIssued({ token: result.token, trackingPath: result.trackingPath })
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusy(false)
      setRotating(false)
    }
  }

  async function copyLink() {
    if (!issued) return
    try {
      await navigator.clipboard.writeText(`${window.location.origin}${issued.trackingPath}`)
      setCopied(true)
    } catch {
      // Clipboard access is refused in some browsers and over plain HTTP. The URL is on screen
      // either way, so failing quietly here is better than reporting an error the seller cannot
      // act on.
      setCopied(false)
    }
  }

  const eta = formatDate(shipment.estimatedArrivalUtc)
  const departed = formatDate(shipment.departedAtUtc)

  return (
    <article className="rounded-xl border border-stone-200">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-stone-100 p-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs text-stone-500">{shipment.reference}</span>
            <ShipmentStatusPill status={shipment.status} />
            <span className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-700">
              {labelOf(SHIPMENT_MODES, shipment.mode)}
            </span>
          </div>
          <p className="mt-1.5 text-sm font-medium text-stone-800">{route(shipment)}</p>
          <p className="mt-0.5 text-xs text-stone-500">
            {shipment.carrierName}
            {shipment.trackingReference ? ` · ${shipment.trackingReference}` : ''}
            {shipment.incoterms !== null ? ' · ' : ''}
            {shipment.incoterms !== null ? <IncotermBadge value={shipment.incoterms} /> : null}
          </p>
        </div>
        <dl className="text-right text-xs text-stone-500">
          {departed ? (
            <div>
              <dt className="inline font-semibold uppercase tracking-wide">Departed </dt>
              <dd className="inline">{departed}</dd>
            </div>
          ) : null}
          {eta ? (
            <div>
              <dt className="inline font-semibold uppercase tracking-wide">ETA </dt>
              <dd className="inline">{eta}</dd>
            </div>
          ) : null}
        </dl>
      </div>

      <div className="p-4">
        <Timeline shipment={shipment} />

        {shipment.isSeller ? (
          <div className="mt-4">
            {error ? <ErrorAlert detail={error.detail} /> : null}

            {adding ? (
              <AppendEventForm
                shipment={shipment}
                onCancel={() => setAdding(false)}
                onAppended={async () => {
                  setAdding(false)
                  await onChanged()
                }}
              />
            ) : (
              <Button size="sm" onClick={() => setAdding(true)}>
                Add a milestone
              </Button>
            )}
          </div>
        ) : null}

        <div className="mt-4 border-t border-stone-100 pt-4">
          {issued ? (
            <div className="rounded-xl border border-brand-200 bg-brand-50 p-3">
              <p className="text-xs font-semibold text-brand-900">Tracking link issued</p>
              <p className="mt-1 break-all font-mono text-xs text-brand-800">
                {window.location.origin}
                {issued.trackingPath}
              </p>
              <p className="mt-1.5 text-xs text-brand-900/80">
                This is the only time the link is shown. The server keeps just a hash of it, so it
                cannot be recovered — copy it now, and if it is ever lost, rotate it below.
              </p>
              <div className="mt-2 flex items-center gap-2">
                <Button size="sm" onClick={copyLink}>
                  {copied ? 'Copied' : 'Copy link'}
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setIssued(null)}>
                  Done
                </Button>
              </div>
            </div>
          ) : (
            <>
              <p className="text-xs text-stone-500">
                {shipment.hasGuestTracking
                  ? 'A guest tracking link is active. Its URL is not readable — only a hash is stored — so it cannot be recovered or recalled.'
                  : 'No guest tracking link. Anyone following this consignment would need an account.'}
              </p>
              {shipment.isSeller ? (
                <div className="mt-2">
                  <Button size="sm" variant="secondary" onClick={() => setRotating(true)}>
                    {shipment.hasGuestTracking ? 'Rotate tracking link' : 'Issue a tracking link'}
                  </Button>
                </div>
              ) : null}
            </>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={rotating}
        title={shipment.hasGuestTracking ? 'Rotate the tracking link?' : 'Issue a tracking link?'}
        confirmLabel={shipment.hasGuestTracking ? 'Rotate link' : 'Issue link'}
        tone={shipment.hasGuestTracking ? 'danger' : 'primary'}
        pending={busy}
        onCancel={() => setRotating(false)}
        onConfirm={handleRotate}
        message={
          shipment.hasGuestTracking
            ? 'The current link stops working immediately. Anyone still holding it loses access, and they would need the new link from you.'
            : 'Anyone with the link can follow this consignment without an account. They will see the route, carrier and milestones — not the order, the parties, the goods or the price.'
        }
      />
    </article>
  )
}

/**
 * The milestones, oldest first, so it reads forwards as a journey rather than as a log to be
 * decoded. Newest is also the current status, which is why it leads.
 */
function Timeline({ shipment }: { shipment: ShipmentDto }) {
  if (shipment.events.length === 0) {
    return (
      <p className="rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
        Nothing recorded yet.
      </p>
    )
  }

  const events = [...shipment.events].reverse()

  return (
    <ol className="space-y-0">
      {events.map((event, index) => {
        const at = formatDateTime(event.occurredAtUtc)
        const entered = formatDateTime(event.recordedAtUtc)
        // A milestone typed in after the fact says something worth surfacing: the date it
        // describes and the date it was entered are both on the record, and quietly conflating
        // them would misstate when the seller knew.
        const lateEntry =
          event.recordedAtUtc.length > 0 &&
          Math.abs(new Date(event.recordedAtUtc).getTime() - new Date(event.occurredAtUtc).getTime()) >
            1000 * 60 * 60 * 24

        return (
          <li key={event.id} className="flex gap-3">
            <div className="flex flex-col items-center">
              <span
                className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${
                  index === 0 ? 'bg-brand-600' : 'bg-stone-300'
                }`}
              />
              {index < events.length - 1 ? <span className="w-px flex-1 bg-stone-200" /> : null}
            </div>
            <div className="min-w-0 flex-1 pb-4">
              <div className="flex flex-wrap items-baseline gap-x-2">
                <span className="text-sm font-medium text-stone-800">
                  {labelOf(SHIPMENT_STATUSES, event.status)}
                </span>
                <span className="text-xs text-stone-500">{at}</span>
              </div>
              {event.location ? (
                <p className="mt-0.5 text-xs text-stone-600">{event.location}</p>
              ) : null}
              {event.description ? (
                <p className="mt-0.5 text-xs text-stone-500">{event.description}</p>
              ) : null}
              {lateEntry ? (
                <p className="mt-0.5 text-xs text-stone-400">
                  Entered {entered} — describing an earlier movement.
                </p>
              ) : null}
            </div>
          </li>
        )
      })}
    </ol>
  )
}

/**
 * The seller-side booking form.
 *
 * Almost every field is optional because the order already carries the route and terms: the common
 * case is a carrier, a mode and a container number. Origin *country* has no default, though, and
 * that is deliberate — the order model records a loading port but no origin country, and a
 * plausible-looking guess on a page the buyer trusts is worse than an honest blank.
 */
function BookShipmentForm({
  order,
  onCancel,
  onBooked,
}: {
  order: OrderDetailDto
  onCancel: () => void
  onBooked: () => Promise<void>
}) {
  const [mode, setMode] = useState(String(SHIPMENT_MODE.Sea))
  const [carrierName, setCarrierName] = useState('')
  const [trackingReference, setTrackingReference] = useState('')
  const [originCountry, setOriginCountry] = useState('')
  const [originPort, setOriginPort] = useState(order.portOfLoading ?? '')
  const [destinationCountry, setDestinationCountry] = useState(order.destinationCountry ?? '')
  const [destinationPort, setDestinationPort] = useState(order.portOfDischarge ?? '')
  const [incoterms, setIncoterms] = useState(String(order.incoterms))
  const [eta, setEta] = useState('')
  const [bookedOn, setBookedOn] = useState('')
  const [guestTracking, setGuestTracking] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const carrierMissing = carrierName.trim().length === 0
  const etaInPast = eta.length > 0 && eta < new Date().toISOString().slice(0, 10)
  const bookedInFuture = bookedOn.length > 0 && bookedOn > new Date().toISOString().slice(0, 10)

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (carrierMissing || etaInPast || bookedInFuture) return

    setBusy(true)
    setError(null)
    try {
      await createShipment(order.id, {
        mode: Number(mode),
        carrierName: carrierName.trim(),
        trackingReference: trackingReference.trim() || undefined,
        originCountry: originCountry.trim() || undefined,
        originPort: originPort.trim() || undefined,
        destinationCountry: destinationCountry.trim() || undefined,
        destinationPort: destinationPort.trim() || undefined,
        incoterms: incoterms === '' ? undefined : Number(incoterms),
        estimatedArrivalUtc: eta ? utcMidnight(eta) : undefined,
        initialOccurredAtUtc: bookedOn ? utcMidnight(bookedOn) : undefined,
        enableGuestTracking: guestTracking,
      })
      await onBooked()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
      <h3 className="text-sm font-semibold text-stone-700">Record a consignment</h3>
      <p className="mt-1 text-xs text-stone-500">
        The booking is written as the first milestone, so the timeline has an origin from the start.
      </p>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'SHIPMENT_ALREADY_EXISTS'
                ? 'This order already has a consignment. Splitting one consignment per order is not supported yet.'
                : error.code === 'ORDER_CLOSED'
                  ? 'This order is closed, so it cannot take a consignment.'
                  : error.detail
            }
          />
        </div>
      ) : null}

      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <SelectField
          label="Mode"
          id="shipment-mode"
          value={mode}
          onChange={(e) => setMode(e.target.value)}
          options={SHIPMENT_MODES}
          values={SHIPMENT_MODES.map((_, index) => index)}
        />
        <Field
          label="Carrier"
          id="shipment-carrier"
          value={carrierName}
          onChange={(e) => setCarrierName(e.target.value)}
          required
          placeholder="Maersk"
          hint={carrierMissing ? 'Who is moving the goods.' : undefined}
        />
        <Field
          label="Tracking reference"
          id="shipment-tracking-ref"
          value={trackingReference}
          onChange={(e) => setTrackingReference(e.target.value)}
          placeholder="MAEU1234567"
          hint="Optional. Container number, air waybill or bill of lading."
        />
        <Field
          label="Estimated arrival"
          id="shipment-eta"
          type="date"
          value={eta}
          onChange={(e) => setEta(e.target.value)}
          hint={
            etaInPast
              ? 'An arrival in the past is not a forecast. Leave it blank if it is not known.'
              : "Optional. The buyer's own expected-delivery date is separate."
          }
        />
        <Field
          label="Origin country"
          id="shipment-origin-country"
          value={originCountry}
          onChange={(e) => setOriginCountry(e.target.value)}
          placeholder="Nigeria"
          hint="Optional. No default — this order records a loading port, not an origin country."
        />
        <Field
          label="Loading port"
          id="shipment-origin-port"
          value={originPort}
          onChange={(e) => setOriginPort(e.target.value)}
          placeholder="Onne (NES)"
          hint="Defaults to this order's port of loading."
        />
        <Field
          label="Destination country"
          id="shipment-destination-country"
          value={destinationCountry}
          onChange={(e) => setDestinationCountry(e.target.value)}
          placeholder="Kenya"
          hint="Defaults to this order's destination country."
        />
        <Field
          label="Discharge port"
          id="shipment-destination-port"
          value={destinationPort}
          onChange={(e) => setDestinationPort(e.target.value)}
          placeholder="Mombasa (MBA)"
          hint="Defaults to this order's port of discharge."
        />
        <SelectField
          label="Incoterm"
          id="shipment-incoterm"
          value={incoterms}
          onChange={(e) => setIncoterms(e.target.value)}
          options={INCOTERMS}
          values={INCOTERMS.map((_, index) => index)}
        />
        <Field
          label="Booked on"
          id="shipment-booked-on"
          type="date"
          value={bookedOn}
          onChange={(e) => setBookedOn(e.target.value)}
          hint={
            bookedInFuture
              ? 'A consignment cannot be booked in the future.'
              : "Defaults to today. Set it if you are logging up a shipment booked earlier — every milestone is sorted against this date."
          }
        />
      </div>

      <label className="mt-4 flex items-start gap-2 text-xs text-stone-600">
        <input
          type="checkbox"
          checked={guestTracking}
          onChange={(e) => setGuestTracking(e.target.checked)}
          className="mt-0.5"
        />
        <span>
          Issue a guest tracking link. Anyone holding it can follow this consignment without an
          account, and sees only the route, carrier and milestones. Turn it off if the customer
          should not be able to follow the goods.
        </span>
      </label>

      <div className="mt-4 flex items-center gap-2">
        <Button type="submit" size="sm" disabled={busy || carrierMissing || etaInPast || bookedInFuture}>
          {busy ? 'Recording…' : 'Record consignment'}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

/**
 * The seller-side milestone form.
 *
 * Statuses are offered in journey order but are not constrained to it — delivered goods do
 * generate a customs exception afterwards, and refusing that entry would only teach the seller
 * to stop recording what happened.
 */
function AppendEventForm({
  shipment,
  onCancel,
  onAppended,
}: {
  shipment: ShipmentDto
  onCancel: () => void
  onAppended: () => Promise<void>
}) {
  const [status, setStatus] = useState(String(SHIPMENT_STATUS.Departed))
  const [location, setLocation] = useState('')
  const [description, setDescription] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const inFuture = occurredOn.length > 0 && occurredOn > new Date().toISOString().slice(0, 10)

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (inFuture) return

    setBusy(true)
    setError(null)
    try {
      await appendTrackingEvent(shipment.id, {
        status: Number(status),
        location: location.trim() || undefined,
        description: description.trim() || undefined,
        occurredAtUtc: occurredOn ? utcMidnight(occurredOn) : undefined,
      })
      await onAppended()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-3 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
      <h4 className="text-sm font-semibold text-stone-700">Add a milestone</h4>
      <p className="mt-1 text-xs text-stone-500">
        Entries are permanent. A mistake is corrected by adding the right milestone, not by
        changing this one.
      </p>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'MILESTONE_IN_FUTURE'
                ? 'A milestone cannot be dated in the future — the shipment has not got there yet.'
                : error.detail
            }
          />
        </div>
      ) : null}

      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <SelectField
          label="Milestone"
          id="event-status"
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          options={SHIPMENT_STATUSES}
          values={SHIPMENT_STATUSES.map((_, index) => index)}
        />
        <Field
          label="Happened on"
          id="event-occurred-on"
          type="date"
          value={occurredOn}
          onChange={(e) => setOccurredOn(e.target.value)}
          hint={
            inFuture
              ? 'The shipment cannot have got there yet.'
              : 'Defaults to today. Set an earlier date when reporting a movement late.'
          }
        />
        <Field
          label="Location"
          id="event-location"
          value={location}
          onChange={(e) => setLocation(e.target.value)}
          placeholder="Mombasa (MBA)"
          hint="Optional. Where it is now."
        />
        <div className="sm:col-span-2">
          <TextArea
            label="Description"
            id="event-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            placeholder="Optional. What the carrier reported."
          />
        </div>
      </div>

      <div className="mt-4 flex items-center gap-2">
        <Button type="submit" size="sm" disabled={busy || inFuture}>
          {busy ? 'Recording…' : 'Add milestone'}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}