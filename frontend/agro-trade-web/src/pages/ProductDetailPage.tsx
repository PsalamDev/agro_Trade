import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerifiedBadge } from '../components/badges'
import { Card, ErrorAlert, Spinner } from '../components/ui'
import { getProduct } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { INCOTERMS, PRICE_UNITS, labelOf } from '../types/trade'
import type { ProductDetailDto } from '../types/trade'

function Attribute({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">{label}</dt>
      <dd className="mt-0.5 text-sm font-medium">{value}</dd>
    </div>
  )
}

export default function ProductDetailPage() {
  const { idOrSlug } = useParams()
  const [product, setProduct] = useState<ProductDetailDto | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!idOrSlug) return
    let cancelled = false
    setLoading(true)
    setError(null)
    getProduct(idOrSlug)
      .then((data) => {
        if (!cancelled) setProduct(data)
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
  }, [idOrSlug])

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to marketplace
        </Link>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Listing not available" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : product ? (
          <div className="mt-6 grid gap-6 lg:grid-cols-5">
            <div className="lg:col-span-3">
              <Card>
                {product.primaryImageUrl ? (
                  <img
                    src={product.primaryImageUrl}
                    alt={product.name}
                    className="h-64 w-full rounded-lg border border-stone-100 object-cover"
                  />
                ) : null}
                <div className="mt-4 flex items-start justify-between gap-3">
                  <div>
                    <h1 className="text-2xl font-bold tracking-tight">{product.name}</h1>
                    <p className="mt-1 text-sm text-stone-500">
                      {product.slug} · {product.hsCode ? `HS ${product.hsCode}` : 'No HS code'}
                    </p>
                  </div>
                  <VerifiedBadge visible={product.isSellerVerifiedBadgeVisible} size="lg" />
                </div>
                {product.description ? <p className="mt-4 text-sm leading-relaxed text-stone-600">{product.description}</p> : null}

                <dl className="mt-6 grid grid-cols-2 gap-4 rounded-lg bg-stone-50 p-4 sm:grid-cols-3">
                  <Attribute label="Price" value={`${product.currency} ${Number(product.price).toLocaleString()} / ${labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}`} />
                  <Attribute label="Available" value={`${Number(product.availableQuantity).toLocaleString()} ${labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}`} />
                  <Attribute label="MOQ" value={`${Number(product.minimumOrderQuantity).toLocaleString()}`} />
                  <Attribute label="Origin" value={`${product.originCountry}${product.portOfLoading ? ` · ${product.portOfLoading}` : ''}`} />
                  <Attribute label="Incoterm" value={product.incoterms !== null && product.incoterms !== undefined ? (INCOTERMS[product.incoterms] ?? '—') : '—'} />
                  <Attribute label="Lead time" value={product.leadTimeDays ? `${product.leadTimeDays} days` : '—'} />
                </dl>
              </Card>

              {product.qualitySpecs && Object.keys(product.qualitySpecs).length > 0 ? (
                <Card className="mt-6">
                  <h2 className="text-lg font-semibold">Quality specifications</h2>
                  <dl className="mt-4 grid gap-3 sm:grid-cols-2">
                    {Object.entries(product.qualitySpecs).map(([key, value]) => (
                      <Attribute key={key} label={key} value={value} />
                    ))}
                  </dl>
                </Card>
              ) : null}

              {product.packagingOptions.length > 0 ? (
                <Card className="mt-6">
                  <h2 className="text-lg font-semibold">Packaging</h2>
                  <ul className="mt-3 flex flex-wrap gap-2">
                    {product.packagingOptions.map((option) => (
                      <li key={option} className="rounded-full bg-stone-100 px-3 py-1 text-sm text-stone-700">
                        {option}
                      </li>
                    ))}
                  </ul>
                </Card>
              ) : null}

              {product.certifications.length > 0 ? (
                <Card className="mt-6">
                  <h2 className="text-lg font-semibold">Certifications</h2>
                  <ul className="mt-3 flex flex-wrap gap-2">
                    {product.certifications.map((cert) => (
                      <li key={cert} className="rounded-full bg-brand-50 px-3 py-1 text-sm font-medium text-brand-800">
                        {cert}
                      </li>
                    ))}
                  </ul>
                </Card>
              ) : null}
            </div>

            <div className="space-y-6 lg:col-span-2">
              <Card>
                <div className="flex items-center justify-between gap-2">
                  <h2 className="text-lg font-semibold">Seller</h2>
                  <VerifiedBadge visible={product.seller.isVerifiedBadgeVisible} />
                </div>
                <Link to={`/businesses/${product.seller.businessId}`} className="mt-2 block text-lg font-semibold text-brand-700 hover:underline">
                  {product.seller.tradingName}
                </Link>
                <dl className="mt-4 grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <dt className="text-xs text-stone-500">Home country</dt>
                    <dd className="font-medium">{product.seller.homeCountry ?? '—'}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-stone-500">Rating</dt>
                    <dd className="font-medium">
                      {product.seller.ratingCount > 0
                        ? `${product.seller.rating.toFixed(1)} ★ (${product.seller.ratingCount})`
                        : 'No reviews yet'}
                    </dd>
                  </div>
                </dl>
                {product.paymentTermsOffered.length > 0 ? (
                  <div className="mt-4">
                    <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Payment terms offered</p>
                    <ul className="mt-2 flex flex-wrap gap-2">
                      {product.paymentTermsOffered.map((term) => (
                        <li key={term} className="rounded-md bg-stone-100 px-2 py-0.5 font-mono text-xs text-stone-700">
                          {term}
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </Card>

              <Card className="bg-brand-50/60">
                <h2 className="text-lg font-semibold">Interested in this supply?</h2>
                <p className="mt-1 text-sm text-stone-600">
                  Quote-ready importers can publish an RFQ and invite this seller to respond with full trade terms.
                </p>
                <Link
                  to="/rfqs"
                  className="mt-4 inline-block rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
                >
                  Browse open RFQs
                </Link>
              </Card>
            </div>
          </div>
        ) : null}
      </main>
    </div>
  )
}