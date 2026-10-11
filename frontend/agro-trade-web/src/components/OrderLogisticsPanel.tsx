import { useCallback, useEffect, useState } from 'react'
import { Button, ErrorAlert, Spinner } from './ui'
import { BookLogisticsForm } from './BookLogisticsForm'
import { LogisticsBookingCard } from './LogisticsBookingCard'
import { getOrderLogisticsBookings } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { ORDER_STATUS } from '../types/trade'
import type { OrderDetailDto } from '../types/trade'
import type { LogisticsBookingDto } from '../types/logistics'

interface Props {
  order: OrderDetailDto
  onChanged: () => Promise<void> | void
}

/**
 * Logistics bookings raised against one order (Phase 4 — LSP directory + booking). Either party to
 * the order may book a provider; both parties see the same bookings and their state.
 *
 * Bookings are fetched here rather than folded into `OrderDetailDto`, mirroring the consignment
 * panel: most order reads never look at them, so they should not be carried along by every one.
 */
export function OrderLogisticsPanel({ order, onChanged }: Props) {
  const [bookings, setBookings] = useState<LogisticsBookingDto[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<{ code?: string; detail?: string } | null>(null)
  const [booking, setBooking] = useState(false)

  const load = useCallback(async () => {
    try {
      setLoadError(null)
      setBookings(await getOrderLogisticsBookings(order.id))
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

  // A draft or cancelled order is closed to new bookings; the API refuses them too.
  const bookable =
    (order.isBuyer || order.isSeller) &&
    order.status !== ORDER_STATUS.Draft &&
    order.status !== ORDER_STATUS.Cancelled

  return (
    <section className="mt-6 border-t border-stone-100 pt-6">
      <h2 className="text-lg font-semibold">Logistics</h2>
      <p className="mt-1 text-sm text-stone-500">
        Freight forwarders and hauliers booked against this order. Find one in the{' '}
        <a href="/logistics" className="font-medium text-brand-700 hover:underline">
          logistics directory
        </a>
        .
      </p>

      {loadError ? (
        <div className="mt-3">
          <ErrorAlert detail={loadError.detail} />
        </div>
      ) : loading ? (
        <div className="mt-3">
          <Spinner label="Loading logistics bookings." />
        </div>
      ) : bookings.length === 0 ? (
        <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-6 text-center text-sm text-stone-500">
          No logistics provider booked for this order yet.
        </p>
      ) : (
        <div className="mt-4 space-y-4">
          {bookings.map((item) => (
            <LogisticsBookingCard key={item.id} booking={item} onChanged={refresh} showOrderLink={false} />
          ))}
        </div>
      )}

      {bookable ? (
        booking ? (
          <div className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
            <h3 className="text-sm font-semibold text-stone-700">Book a logistics provider</h3>
            <div className="mt-3">
              <BookLogisticsForm
                orderId={order.id}
                orderNumber={order.orderNumber}
                onBooked={async () => {
                  setBooking(false)
                  await refresh()
                }}
                onClose={() => setBooking(false)}
              />
            </div>
          </div>
        ) : (
          <div className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
            <h3 className="text-sm font-semibold text-stone-700">Need help moving the goods?</h3>
            <p className="mt-1 text-xs text-stone-500">
              Book a vetted freight forwarder, haulier or customs broker against this order.
            </p>
            <div className="mt-3">
              <Button size="sm" onClick={() => setBooking(true)}>
                Book logistics
              </Button>
            </div>
          </div>
        )
      ) : null}
    </section>
  )
}
