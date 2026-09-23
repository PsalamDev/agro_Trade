import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { RfqCard } from '../components/RfqCard'
import { EmptyState, ErrorAlert, Field, SelectField, Spinner } from '../components/ui'
import { getCategoryTree, getOpenRfqs } from '../lib/trade'
import { problemFrom } from '../lib/api'
import type { CategoryDto, TradeRequestCardDto } from '../types/trade'

export default function RfqDirectoryPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [rfqs, setRfqs] = useState<TradeRequestCardDto[]>([])
  const [categories, setCategories] = useState<CategoryDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

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
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Open RFQs</h1>
            <p className="mt-1 text-sm text-stone-500">
              Importers’ sourcing requests — quote with full trade terms, or publish your own.
            </p>
          </div>
        </div>

        <div className="mt-6 grid gap-4 rounded-xl border border-stone-200 bg-white p-5 shadow-sm sm:grid-cols-3">
          <Field
            label="Search"
            id="rfq-q"
            placeholder="e.g. white maize"
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
          <Field
            label="Destination country (ISO-2)"
            id="rfq-destination"
            maxLength={2}
            placeholder="NG"
            value={destinationCountry}
            onChange={(e) => void updateParam('destinationCountry', e.target.value.toUpperCase())}
          />
        </div>

        {error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load RFQs" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-8">
            <Spinner />
          </div>
        ) : rfqs.length === 0 ? (
          <div className="mt-8">
            <EmptyState title="No open RFQs match" body="Check back soon — or publish your own sourcing request." />
          </div>
        ) : (
          <div className="mt-8 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {rfqs.map((rfq) => (
              <RfqCard key={rfq.id} rfq={rfq} />
            ))}
          </div>
        )}
      </main>
    </div>
  )
}