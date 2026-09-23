import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ListingPill, VerifiedBadge } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { deleteProduct, getMyBusinesses, searchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { PRICE_UNITS, labelOf } from '../types/trade'
import type { BusinessSummaryDto, ProductCardDto } from '../types/trade'

export default function MyProductsPage() {
  const navigate = useNavigate()
  const [products, setProducts] = useState<ProductCardDto[] | null>(null)
  const [businesses, setBusinesses] = useState<BusinessSummaryDto[]>([])
  const [businessId, setBusinessId] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      const [mine, list] = await Promise.all([getMyBusinesses(), searchProducts({ sellerBusinessId: businessId || undefined, pageSize: 50 })])
      setBusinesses(mine)
      setProducts(list.items)
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [businessId])

  useEffect(() => {
    void load()
  }, [load])

  async function handleDelete(productId: string) {
    setDeletingId(productId)
    setError(null)
    try {
      await deleteProduct(productId)
      await load()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">My listings</h1>
            <p className="mt-1 text-sm text-stone-500">Manage your export supply. Active listings require a verified business.</p>
          </div>
          <Link
            to="/products/new"
            className="rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
          >
            New listing
          </Link>
        </div>

        {businesses.length > 1 ? (
          <div className="mt-6 flex max-w-sm items-center justify-between gap-3">
            <select
              value={businessId}
              onChange={(e) => setBusinessId(e.target.value)}
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
            >
              <option value="">All my businesses</option>
              {businesses.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.tradingName}
                </option>
              ))}
            </select>
          </div>
        ) : null}

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load listings" detail={error.detail} />
          </div>
        ) : null}

        {products === null ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : products.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="No listings yet"
              body="Create your first export listing — it will stay Draft until your business is verified."
            />
          </div>
        ) : (
          <ul className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((product) => (
              <li key={product.id}>
                <Card className="flex h-full flex-col">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <Link to={`/products/${product.slug || product.id}`} className="font-semibold text-stone-900 hover:text-brand-700">
                        {product.name}
                      </Link>
                      <p className="text-sm text-stone-500">
                        {product.currency} {Number(product.price).toLocaleString()} / {labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}
                      </p>
                    </div>
                    <div className="flex shrink-0 flex-col items-end gap-1">
                      <ListingPill status={product.listingStatus} />
                      <VerifiedBadge visible={product.isSellerVerifiedBadgeVisible} />
                    </div>
                  </div>
                  <p className="mt-3 text-sm text-stone-500">
                    {product.originCountry}
                    {product.portOfLoading ? ` · ${product.portOfLoading}` : ''} · {product.sellerTradingName}
                  </p>
                  <div className="mt-4 flex flex-1 items-end gap-2">
                    <button
                      type="button"
                      onClick={() => navigate(`/products/${product.slug || product.id}/edit`)}
                      className="flex-1 rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50"
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      disabled={deletingId === product.id}
                      onClick={() => void handleDelete(product.id)}
                      className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-700 transition hover:bg-red-100 disabled:opacity-60"
                    >
                      {deletingId === product.id ? '…' : 'Delete'}
                    </button>
                  </div>
                </Card>
              </li>
            ))}
          </ul>
        )}
      </main>
    </div>
  )
}