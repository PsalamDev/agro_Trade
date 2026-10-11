import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerifiedBadge } from '../components/badges'
import { BookLogisticsForm } from '../components/BookLogisticsForm'
import { Stars } from '../components/Stars'
import { Button, Card, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { ArrowLeftIcon, GlobeIcon, MailIcon, MapPinIcon, PhoneIcon, TruckIcon } from '../components/icons'
import { countryName } from '../lib/trade'
import { formatUtc } from '../lib/format'
import { getLogisticsProvider, getLogisticsProviderRatings } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { modeLabel, serviceLabels } from '../types/logistics'
import type { LogisticsProviderDetailDto, LogisticsRatingDto } from '../types/logistics'

export default function LogisticsProviderPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  const [provider, setProvider] = useState<LogisticsProviderDetailDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [booked, setBooked] = useState(false)
  const [ratings, setRatings] = useState<LogisticsRatingDto[]>([])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    getLogisticsProvider(id)
      .then((result) => {
        if (!cancelled) setProvider(result)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  // Ratings are a second, authenticated read. A failure here is not fatal to the page — the profile
  // is still worth showing — so it degrades to "no ratings" rather than an error state.
  useEffect(() => {
    let cancelled = false
    getLogisticsProviderRatings(id, { pageSize: 10 })
      .then((page) => {
        if (!cancelled) setRatings(page.items)
      })
      .catch(() => {
        if (!cancelled) setRatings([])
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const services = serviceLabels(provider?.services)

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-10">
        <Link to="/logistics" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-700 hover:underline">
          <ArrowLeftIcon className="h-4 w-4" />
          Back to the directory
        </Link>

        {loading ? (
          <div className="mt-8">
            <Spinner label="Loading the provider." />
          </div>
        ) : error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load this provider" detail={error.detail} />
          </div>
        ) : !provider ? null : (
          <div className="mt-6 grid gap-6 lg:grid-cols-3">
            <div className="lg:col-span-2">
              <PageHeader
                eyebrow="Logistics provider"
                title={provider.companyName}
                subtitle={provider.description ?? undefined}
                action={<VerifiedBadge visible={provider.isVerified} size="lg" />}
              />

              <div className="mt-6 grid gap-4 sm:grid-cols-2">
                <Card>
                  <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Home base</h3>
                  <p className="mt-1 flex items-center gap-2 font-medium">
                    <MapPinIcon className="h-4 w-4 text-stone-400" />
                    {countryName(provider.homeCountry)}
                  </p>
                </Card>
                <Card>
                  <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Modes</h3>
                  <p className="mt-1 flex items-center gap-2 font-medium">
                    <TruckIcon className="h-4 w-4 text-stone-400" />
                    {provider.modes.map(modeLabel).join(', ') || 'Not specified'}
                  </p>
                </Card>
              </div>

              <div className="mt-4 grid gap-4 sm:grid-cols-2">
                <Card>
                  <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Services</h3>
                  {services.length ? (
                    <div className="mt-2 flex flex-wrap gap-1.5">
                      {services.map((service) => (
                        <span key={service} className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-700">
                          {service}
                        </span>
                      ))}
                    </div>
                  ) : (
                    <p className="mt-1 text-sm text-stone-500">Not specified</p>
                  )}
                </Card>
                <Card>
                  <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Coverage</h3>
                  {provider.coverage.length ? (
                    <p className="mt-1 text-sm text-stone-700">
                      {provider.coverage.map(countryName).join(', ')}
                    </p>
                  ) : (
                    <p className="mt-1 text-sm text-stone-500">On request</p>
                  )}
                </Card>
              </div>

              <Card className="mt-4">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Contact</h3>
                <div className="mt-2 space-y-1.5 text-sm">
                  {provider.contactEmail ? (
                    <p className="flex items-center gap-2">
                      <MailIcon className="h-4 w-4 text-stone-400" />
                      <a href={`mailto:${provider.contactEmail}`} className="text-brand-700 hover:underline">
                        {provider.contactEmail}
                      </a>
                    </p>
                  ) : null}
                  {provider.contactPhone ? (
                    <p className="flex items-center gap-2">
                      <PhoneIcon className="h-4 w-4 text-stone-400" />
                      {provider.contactPhone}
                    </p>
                  ) : null}
                  {provider.website ? (
                    <p className="flex items-center gap-2">
                      <GlobeIcon className="h-4 w-4 text-stone-400" />
                      <a
                        href={provider.website}
                        target="_blank"
                        rel="noreferrer"
                        className="text-brand-700 hover:underline"
                      >
                        {provider.website}
                      </a>
                    </p>
                  ) : null}
                  {!provider.contactEmail && !provider.contactPhone && !provider.website ? (
                    <p className="text-stone-500">This provider has not published contact details.</p>
                  ) : null}
                </div>
              </Card>

              <Card className="mt-4">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Reputation</h3>
                <div className="mt-2 flex flex-wrap items-center gap-3">
                  <span className="text-2xl font-semibold text-stone-900">{provider.rating.toFixed(1)}</span>
                  <Stars value={provider.rating} />
                  <span className="text-sm text-stone-500">
                    {provider.ratingCount} rating{provider.ratingCount === 1 ? '' : 's'}
                  </span>
                </div>
                {ratings.length ? (
                  <ul className="mt-4 space-y-3">
                    {ratings.map((rating) => (
                      <li key={rating.id} className="border-t border-stone-100 pt-3 first:border-t-0 first:pt-0">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-sm font-medium text-stone-700">
                            {rating.ratedByDisplayName}
                            {rating.isMine ? ' (you)' : ''}
                          </span>
                          <Stars value={rating.rating} size="sm" />
                        </div>
                        {rating.comment ? (
                          <p className="mt-1 whitespace-pre-line text-sm text-stone-600">{rating.comment}</p>
                        ) : null}
                        <p className="mt-0.5 text-xs text-stone-400">{formatUtc(rating.createdAtUtc)}</p>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-3 text-sm text-stone-500">
                    No ratings yet. They appear once a party rates a completed booking.
                  </p>
                )}
              </Card>

              {provider.isMine ? (
                <div className="mt-4">
                  <Link to="/logistics/profile">
                    <Button variant="secondary">Edit your provider profile</Button>
                  </Link>
                </div>
              ) : null}
            </div>

            <div className="lg:col-span-1">
              <Card className="sticky top-6">
                <h2 className="text-lg font-semibold">Book this provider</h2>
                {!user ? (
                  <>
                    <p className="mt-1 text-sm text-stone-500">
                      Sign in to raise a booking against one of your orders.
                    </p>
                    <Link to="/login" className="mt-3 inline-block">
                      <Button>Sign in</Button>
                    </Link>
                  </>
                ) : booked ? (
                  <>
                    <p className="mt-1 text-sm text-stone-600">
                      Booking requested. Track its progress under{' '}
                      <Link to="/logistics/bookings" className="font-medium text-brand-700 hover:underline">
                        your logistics bookings
                      </Link>
                      .
                    </p>
                    <Button variant="secondary" className="mt-3" onClick={() => setBooked(false)}>
                      Book again
                    </Button>
                  </>
                ) : (
                  <>
                    <p className="mt-1 text-sm text-stone-500">
                      Choose one of your live orders and the mode you need. The provider replies to your request.
                    </p>
                    <div className="mt-4">
                      <BookLogisticsForm
                        providerId={provider.id}
                        providerName={provider.companyName}
                        providerModes={provider.modes}
                        onBooked={() => setBooked(true)}
                      />
                    </div>
                  </>
                )}
              </Card>
            </div>
          </div>
        )}
      </main>
    </div>
  )
}
