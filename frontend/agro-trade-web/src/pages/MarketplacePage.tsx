import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ProductCard } from '../components/ProductCard'
import {
  Button,
  EmptyState,
  ErrorAlert,
  Field,
  PageHeader,
  SelectField,
  SkeletonGrid,
} from '../components/ui'
import { getCategoryTree, searchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import type { CategoryDto, ProductCardDto } from '../types/trade'
import { CoinsIcon, FilterIcon, GlobeIcon, SearchIcon, ShieldCheckIcon, XIcon } from '../components/icons'

export default function MarketplacePage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [products, setProducts] = useState<ProductCardDto[]>([])
  const [total, setTotal] = useState(0)
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
        if (!cancelled) {
          setProducts(result.items)
          setTotal(result.total)
        }
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

  const hasFilters = Boolean(
    q || categoryId || originCountry || exportsToCountry || verifiedOnly || minPrice || maxPrice,
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
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Marketplace"
          title="Products"
          subtitle="Browse export listings — search commodities, then compare price, quantity, and trade terms."
          action={
            hasFilters ? (
              <Button variant="secondary" size="sm" onClick={clearAll}>
                <XIcon className="h-3.5 w-3.5" />
                Clear all filters
              </Button>
            ) : undefined
          }
        />

        <form
          onSubmit={handleSubmit}
          className="mt-6 rounded-2xl border border-stone-200 bg-white p-5 shadow-card"
        >
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1">
              <label htmlFor="marketplace-q" className="mb-1.5 block text-sm font-medium text-stone-700">
                Search products
              </label>
              <div className="relative">
                <SearchIcon className="pointer-events-none absolute top-1/2 left-3.5 h-4 w-4 -translate-y-1/2 text-stone-400" />
                <input
                  id="marketplace-q"
                  name="q"
                  type="search"
                  placeholder="e.g. cocoa, cashew, maize…"
                  value={q}
                  onChange={(e) => void updateParam('q', e.target.value)}
                  className="w-full rounded-xl border border-stone-300 bg-white py-2.5 pr-4 pl-10 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                />
              </div>
            </div>
            <Button type="submit" className="h-[42px] px-5">
              <SearchIcon className="h-4 w-4" />
              Search
            </Button>
          </div>

          <div className="mt-5 border-t border-stone-100 pt-5">
            <p className="mb-4 flex items-center gap-1.5 text-xs font-semibold tracking-widest text-stone-400 uppercase">
              <FilterIcon className="h-3.5 w-3.5" />
              Refine
            </p>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
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
                icon={<GlobeIcon className="h-4 w-4" />}
                hint="Two-letter country code"
                value={originCountry}
                onChange={(e) => void updateParam('originCountry', e.target.value.toUpperCase())}
              />
              <Field
                label="Exports to (destination)"
                id="marketplace-dest"
                maxLength={2}
                placeholder="NG"
                icon={<GlobeIcon className="h-4 w-4" />}
                hint="Two-letter country code"
                value={exportsToCountry}
                onChange={(e) => void updateParam('exportsToCountry', e.target.value.toUpperCase())}
              />
              <Field
                label="Min price"
                id="marketplace-min"
                type="number"
                min="0"
                placeholder="0"
                icon={<CoinsIcon className="h-4 w-4" />}
                value={minPrice}
                onChange={(e) => void updateParam('minPrice', e.target.value)}
              />
              <Field
                label="Max price"
                id="marketplace-max"
                type="number"
                min="0"
                placeholder="100000"
                icon={<CoinsIcon className="h-4 w-4" />}
                value={maxPrice}
                onChange={(e) => void updateParam('maxPrice', e.target.value)}
              />
              <label className="flex h-[42px] cursor-pointer items-center gap-2.5 self-end rounded-xl border border-stone-300 bg-stone-50 px-3.5 text-sm font-medium text-stone-700 transition hover:bg-stone-100">
                <input
                  type="checkbox"
                  checked={verifiedOnly}
                  onChange={(e) => updateParam('verifiedOnly', e.target.checked ? '1' : '')}
                  className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
                />
                <ShieldCheckIcon className="h-4 w-4 text-stone-400" />
                Verified sellers only
              </label>
            </div>
          </div>
        </form>

        {error ? (
          <div className="mt-8">
            <ErrorAlert title="Could not load the marketplace" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <SkeletonGrid className="mt-8" />
        ) : products.length === 0 ? (
          <div className="mt-8">
            <EmptyState
              title="No listings match your search"
              body="Try a broader commodity or clear the filters to see everything we ship."
              icon={<SearchIcon className="h-5 w-5" />}
              action={
                hasFilters ? (
                  <Button variant="secondary" onClick={clearAll}>
                    <XIcon className="h-4 w-4" />
                    Clear filters
                  </Button>
                ) : undefined
              }
            />
          </div>
        ) : (
          <>
            <div className="mt-8 flex items-center gap-2">
              <span className="rounded-full bg-brand-100 px-3 py-1 text-sm font-semibold text-brand-800">
                {total} product{total === 1 ? '' : 's'}
              </span>
              {hasFilters ? <span className="text-sm text-stone-500">matching your filters</span> : null}
            </div>
            <div className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {products.map((product) => (
                <ProductCard key={product.id} product={product} />
              ))}
            </div>
          </>
        )}
      </main>
    </div>
  )
}
