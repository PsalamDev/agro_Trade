import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerifiedBadge } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { getBusiness, searchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { BUSINESS_TYPES, labelOf } from '../types/trade'
import type { BusinessDetailDto, ProductCardDto } from '../types/trade'
import { ProductCard } from '../components/ProductCard'

export default function PublicBusinessPage() {
  const { id } = useParams()
  const [business, setBusiness] = useState<BusinessDetailDto | null>(null)
  const [products, setProducts] = useState<ProductCardDto[]>([])
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!id) return
    let cancelled = false
    setLoading(true)
    setError(null)
    Promise.all([getBusiness(id), searchProducts({ sellerBusinessId: id, pageSize: 24 })])
      .then(([biz, result]) => {
        if (cancelled) return
        setBusiness(biz)
        setProducts(result.items)
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

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to marketplace
        </Link>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Business profile not available" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : business ? (
          <>
            <Card className="mt-6">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h1 className="text-2xl font-bold tracking-tight">{business.tradingName}</h1>
                    <VerifiedBadge visible={business.isVerifiedBadgeVisible} size="lg" />
                  </div>
                  <p className="mt-1 text-sm text-stone-500">
                    {business.legalName} · {labelOf(BUSINESS_TYPES, business.businessType)}
                  </p>
                  {business.website ? (
                    <a href={business.website} target="_blank" rel="noreferrer" className="mt-1 block text-sm text-brand-700 hover:underline">
                      {business.website}
                    </a>
                  ) : null}
                </div>
                <div className="text-sm text-stone-500">
                  <p>{business.ratingCount > 0 ? `${business.rating.toFixed(1)} ★ (${business.ratingCount})` : 'No reviews yet'}</p>
                </div>
              </div>

              {business.description ? <p className="mt-4 text-sm leading-relaxed text-stone-600">{business.description}</p> : null}

              {business.exportsTo.length > 0 || business.importsFrom.length > 0 ? (
                <div className="mt-5 flex flex-wrap gap-2 text-xs">
                  {business.exportsTo.length > 0 ? (
                    <span className="rounded-full bg-brand-50 px-2 py-0.5 font-medium text-brand-800">
                      Exports to {business.exportsTo.join(', ')}
                    </span>
                  ) : null}
                  {business.importsFrom.length > 0 ? (
                    <span className="rounded-full bg-blue-50 px-2 py-0.5 font-medium text-blue-800">
                      Imports from {business.importsFrom.join(', ')}
                    </span>
                  ) : null}
                </div>
              ) : null}
            </Card>

            <h2 className="mt-10 text-xl font-bold tracking-tight">Active listings</h2>
            {products.length === 0 ? (
              <div className="mt-4">
                <EmptyState title="No public listings" body="Listings appear here once published and (if required) verified." />
              </div>
            ) : (
              <div className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
                {products.map((product) => (
                  <ProductCard key={product.id} product={product} />
                ))}
              </div>
            )}
          </>
        ) : null}
      </main>
    </div>
  )
}