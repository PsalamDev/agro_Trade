import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { VerifiedBadge } from '../components/badges'
import { EmptyState, ErrorAlert, Field, PageHeader, SelectField, SkeletonGrid } from '../components/ui'
import { SearchIcon, StarIcon, TruckIcon } from '../components/icons'
import { countryName } from '../lib/trade'
import { searchLogisticsProviders } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { LOGISTICS_SERVICE_OPTIONS, SHIPMENT_MODES, modeLabel, serviceLabels } from '../types/logistics'
import type { LogisticsProviderCardDto } from '../types/logistics'

export default function LogisticsDirectoryPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [providers, setProviders] = useState<LogisticsProviderCardDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const q = searchParams.get('q') ?? ''
  const country = searchParams.get('country') ?? ''
  const mode = searchParams.get('mode') ?? ''
  const service = searchParams.get('service') ?? ''

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    searchLogisticsProviders({
      q: q || undefined,
      country: country || undefined,
      mode: mode === '' ? undefined : Number(mode),
      service: service === '' ? undefined : Number(service),
      pageSize: 24,
    })
      .then((result) => {
        if (!cancelled) setProviders(result.items)
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
  }, [q, country, mode, service])

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearchParams(next)
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Logistics directory"
          title="Find a logistics provider"
          subtitle="Freight forwarders, hauliers and customs brokers vetted by the platform. Book one against any live order."
        />

        <div className="mt-6 grid gap-4 rounded-2xl border border-stone-200 bg-white p-5 shadow-card sm:grid-cols-2 lg:grid-cols-4">
          <Field
            label="Search"
            id="lsp-q"
            placeholder="e.g. Lagos freight"
            icon={<SearchIcon className="h-4 w-4" />}
            value={q}
            onChange={(e) => void updateParam('q', e.target.value)}
          />
          <CountrySelect
            id="lsp-country"
            label="Serves country"
            value={country}
            onChange={(code) => void updateParam('country', code)}
            allowClear
            placeholder="Any country"
          />
          <SelectField
            label="Mode"
            id="lsp-mode"
            options={SHIPMENT_MODES.map((_, index) => modeLabel(index))}
            values={SHIPMENT_MODES.map((_, index) => index)}
            value={mode}
            onChange={(e) => void updateParam('mode', e.target.value)}
          />
          <SelectField
            label="Service"
            id="lsp-service"
            options={LOGISTICS_SERVICE_OPTIONS.map((option) => option.label)}
            values={LOGISTICS_SERVICE_OPTIONS.map((option) => option.value)}
            value={service}
            onChange={(e) => void updateParam('service', e.target.value)}
          />
        </div>

        {error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load the directory" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <SkeletonGrid className="mt-8" />
        ) : providers.length === 0 ? (
          <div className="mt-8">
            <EmptyState
              title="No providers match"
              body="Try widening the search — clear a filter or search by a different service."
              icon={<TruckIcon className="h-5 w-5" />}
            />
          </div>
        ) : (
          <>
            <p className="mt-8 text-sm text-stone-500">
              <span className="rounded-full bg-brand-100 px-3 py-1 text-sm font-semibold text-brand-800">
                {providers.length} provider{providers.length === 1 ? '' : 's'}
              </span>
            </p>
            <div className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {providers.map((provider) => (
                <ProviderCard key={provider.id} provider={provider} />
              ))}
            </div>
          </>
        )}
      </main>
    </div>
  )
}

function ProviderCard({ provider }: { provider: LogisticsProviderCardDto }) {
  const services = serviceLabels(provider.services)
  return (
    <Link
      to={`/logistics/providers/${provider.id}`}
      className="group flex flex-col rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft hover:-translate-y-0.5 hover:border-brand-200 hover:shadow-lift"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-display text-base font-semibold text-stone-900">{provider.companyName}</p>
          <p className="mt-0.5 text-xs text-stone-500">{countryName(provider.homeCountry)}</p>
        </div>
        <VerifiedBadge visible={provider.isVerified} />
      </div>

      {provider.description ? (
        <p className="mt-3 line-clamp-3 text-sm text-stone-600">{provider.description}</p>
      ) : null}

      <div className="mt-4 flex flex-wrap gap-1.5">
        {provider.modes.map((mode) => (
          <span
            key={mode}
            className="rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700"
          >
            {modeLabel(mode)}
          </span>
        ))}
        {services.map((service) => (
          <span key={service} className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-600">
            {service}
          </span>
        ))}
      </div>

      <div className="mt-auto flex items-center justify-between pt-4 text-xs text-stone-500">
        <span>
          {provider.coverage.length
            ? `Serves ${provider.coverage.map(countryName).join(', ')}`
            : 'Coverage on request'}
        </span>
        {provider.ratingCount > 0 ? (
          <span className="inline-flex items-center gap-1 font-semibold text-amber-600">
            <StarIcon className="h-3.5 w-3.5" />
            {provider.rating.toFixed(1)}
          </span>
        ) : null}
      </div>
    </Link>
  )
}
