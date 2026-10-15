import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { RfqCard } from '../components/RfqCard'
import { EmptyState, ErrorAlert, Field, PageHeader, SelectField, SkeletonGrid } from '../components/ui'
import { getCategoryTree, getOpenRfqs } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { BUYER_ROLES } from '../types/auth'
import type { CategoryDto, TradeRequestCardDto } from '../types/trade'
import { SearchIcon } from '../components/icons'

export default function RfqDirectoryPage() {
  const { user } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const [rfqs, setRfqs] = useState<TradeRequestCardDto[]>([])
  const [categories, setCategories] = useState<CategoryDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const canBuy = (user?.roles ?? []).some((role) => BUYER_ROLES.includes(role))

  const q = searchParams.get('q') ?? ''
  const categoryId = searchParams.get('categoryId') ?? ''
  const destinationCountry = searchParams.get('destinationCountry') ?? ''

  useEffect(() => {
    getCategoryTree().then(setCategories).catch(() => undefined)
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    getOpenRfqs({
      q: q || undefined,
      categoryId: categoryId || undefined,
      destinationCountry: destinationCountry || undefined,
      pageSize: 24,
    })
      .then((result) => {
        if (!cancelled) setRfqs(result.items)
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
  }, [q, categoryId, destinationCountry])

  const leafCategories = useMemo(
    () => categories.flatMap(({ children }) => children.map((child) => child)),
    [categories],
  )

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
          eyebrow="Sourcing requests"
          title="Open RFQs"
          subtitle="Buyers’ sourcing requests — quote with full trade terms, or publish your own."
          action={
            canBuy ? (
              <Link
                to="/rfqs/new"
                className="inline-flex h-10 shrink-0 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700"
              >
                Publish RFQ
              </Link>
            ) : undefined
          }
        />

        <div className="mt-6 grid gap-4 rounded-2xl border border-stone-200 bg-white p-5 shadow-card sm:grid-cols-3">
          <Field
            label="Search"
            id="rfq-q"
            placeholder="e.g. white maize"
            icon={<SearchIcon className="h-4 w-4" />}
            value={q}
            onChange={(e) => void updateParam('q', e.target.value)}
          />
          <SelectField
            label="Category"
            id="rfq-category"
            options={leafCategories.map((c) => c.name)}
            values={leafCategories.map((c) => c.id)}
            value={categoryId}
            onChange={(e) => void updateParam('categoryId', e.target.value)}
          />
          <CountrySelect
            id="rfq-destination"
            label="Destination country"
            value={destinationCountry}
            onChange={(code) => void updateParam('destinationCountry', code)}
            allowClear
            placeholder="Any country"
          />
        </div>

        {error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load RFQs" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <SkeletonGrid className="mt-8" />
        ) : rfqs.length === 0 ? (
          <div className="mt-8">
            <EmptyState
              title="No open RFQs match"
              body="Check back soon — or publish your own sourcing request."
              icon={<SearchIcon className="h-5 w-5" />}
            />
          </div>
        ) : (
          <>
            <p className="mt-8 text-sm text-stone-500">
              <span className="rounded-full bg-brand-100 px-3 py-1 text-sm font-semibold text-brand-800">
                {rfqs.length} open request{rfqs.length === 1 ? '' : 's'}
              </span>
            </p>
            <div className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {rfqs.map((rfq) => (
                <RfqCard key={rfq.id} rfq={rfq} />
              ))}
            </div>
          </>
        )}
      </main>
    </div>
  )
}