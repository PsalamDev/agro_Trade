import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { Button, EmptyState, ErrorAlert, PageHeader, SelectField, SkeletonGrid } from '../components/ui'
import { LogisticsBookingCard } from '../components/LogisticsBookingCard'
import { TruckIcon } from '../components/icons'
import { getMyLogisticsBookings } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { LOGISTICS_BOOKING_STATUS_LABELS } from '../types/logistics'
import type { LogisticsBookingDto } from '../types/logistics'

export default function LogisticsBookingsPage() {
  const [bookings, setBookings] = useState<LogisticsBookingDto[]>([])
  const [statusFilter, setStatusFilter] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const result = await getMyLogisticsBookings({
        status: statusFilter === '' ? undefined : Number(statusFilter),
        pageSize: 50,
      })
      setBookings(result.items)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [statusFilter])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-4xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Logistics"
          title="Logistics bookings"
          subtitle="Every booking you raised as a buyer or seller, and every request naming you as the provider."
        />

        <div className="mt-6 max-w-xs">
          <SelectField
            label="Status"
            id="booking-status-filter"
            options={['Any status', ...LOGISTICS_BOOKING_STATUS_LABELS]}
            values={['', '0', '1', '2', '3', '4']}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
          />
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load your bookings" detail={error.detail} />
          </div>
        ) : loading ? (
          <SkeletonGrid className="mt-6" count={3} />
        ) : bookings.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="No logistics bookings yet"
              body="Find a provider in the directory and book them against a live order."
              icon={<TruckIcon className="h-5 w-5" />}
              action={
                <Link to="/logistics">
                  <Button>Browse the directory</Button>
                </Link>
              }
            />
          </div>
        ) : (
          <div className="mt-6 space-y-4">
            {bookings.map((booking) => (
              <LogisticsBookingCard key={booking.id} booking={booking} onChanged={load} />
            ))}
          </div>
        )}
      </main>
    </div>
  )
}
