import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ProductCard } from '../components/ProductCard'
import { EmptyState, ErrorAlert, Field, SelectField, Spinner } from '../components/ui'
import { getCategoryTree, searchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import type { CategoryDto, ProductCardDto } from '../types/trade'

export default function MarketplacePage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [products, setProducts] = useState<ProductCardDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [categories, setCategories] = useState<CategoryDto[]>([])

  const q = searchParams.get('q') ?? ''
  const categoryId = searchParams.get('categoryId') ?? ''
  const originCountry = searchParams.get('originCountry') ?? ''
  const exportsToCountry = searchParams.get('exportsToCountry') ?? ''
  const verifiedOnly = searchParams.get('verifiedOnly') === '1'
  const minPrice = searchParams.get('minPrice') ?? ''
  const maxPrice = searchParams.get('maxPrice') ?? ''

  useEffect(() => {
    getCategoryTree().then(setCategories).catch(() => undefined)
  }, [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    searchProducts({
      q: q || undefined,
      categoryId: categoryId || undefined,
      originCountry: originCountry || undefined,
      exportsToCountry: exportsToCountry || undefined,
      verifiedOnly: verifiedOnly || undefined,
      minPrice: minPrice ? Number(minPrice) : undefined,
      maxPrice: maxPrice ? Number(maxPrice) : undefined,
      pageSize: 24,
    })
      .then((result) => {
        if (!cancelled) setProducts(result.items)
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
  }, [q, categoryId, originCountry, exportsToCountry, verifiedOnly, minPrice, maxPrice])

  const leafCategories = useMemo(
    () => categories.flatMap(({ children }) => children.map((child) => ({ ...child, parentName: child.name }))),
    [categories],
  )

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearchParams(next)
  }

  function clearAll() {
    setSearchParams(new URLSearchParams())
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const form = new FormData(event.currentTarget as HTMLFormElement)
    updateParam('q', String(form.get('q') ?? ''))
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Marketplace</h1>
            <p className="mt-1 text-sm text-stone-500">
              Verified export listings from across corridors — filter by origin, destination, and price.
            </p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="mt-6 grid gap-4 rounded-xl border border-stone-200 bg-white p-5 shadow-sm lg:grid-cols-4">
          <Field label="Search" id="marketplace-q" placeholder="e.g. cashew, arabica" value={q} onChange={(e) => void updateParam('q', e.target.value)} />
          <SelectField
            label="Category"
            id="marketplace-category"
            options={leafCategories.map((c) => c.name)}
            values={leafCategories.map((c) => c.id)}
            value={categoryId}
            onChange={(e) => void updateParam('categoryId', e.target.value)}
          />
          <Field
            label="Origin country (ISO-2)"
            id="marketplace-origin"
            maxLength={2}
            placeholder="KE"
            value={originCountry}
            onChange={(e) => void updateParam('originCountry', e.target.value.toUpperCase())}
          />
          <Field
            label="Exports to (destination)"
            id="marketplace-dest"
            maxLength={2}
            placeholder="NG"
            value={exportsToCountry}
            onChange={(e) => void updateParam('exportsToCountry', e.target.value.toUpperCase())}
          />
          <Field label="Min price" id="marketplace-min" type="number" min="0" placeholder="0" value={minPrice} onChange={(e) => void updateParam('minPrice', e.target.value)} />
          <Field label="Max price" id="marketplace-max" type="number" min="0" placeholder="100000" value={maxPrice} onChange={(e) => void updateParam('maxPrice', e.target.value)} />
          <label className="flex items-center gap-2 self-end pb-2 text-sm font-medium text-stone-700">
            <input
              type="checkbox"
              checked={verifiedOnly}
              onChange={(e) => updateParam('verifiedOnly', e.target.checked ? '1' : '')}
              className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
            />
            Verified sellers only
          </label>
          <button
            type="button"
            onClick={clearAll}
            className="self-end rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-600 transition hover:bg-stone-50"
          >
            Clear filters
          </button>
        </form>

        {error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load the marketplace" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-8">
            <Spinner />
          </div>
        ) : products.length === 0 ? (
          <div className="mt-8">
            <EmptyState title="No listings match your filters" body="Try clearing filters or searching a broader commodity." />
          </div>
        ) : (
          <div className="mt-8 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((product) => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>
        )}
      </main>
    </div>
  )
}