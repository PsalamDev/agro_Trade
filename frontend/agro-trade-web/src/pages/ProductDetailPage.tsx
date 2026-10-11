import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerifiedBadge } from '../components/badges'
import { Card, ErrorAlert, Spinner, Button } from '../components/ui'
import { TabPanel, Tabs } from '../components/overlays'
import { AwardIcon, CartIcon, CoinsIcon, PackageIcon } from '../components/icons'
import { addToCart, getProduct } from '../lib/trade'
import { useAuth } from '../lib/auth-context'
import { useCart } from '../lib/cart-context'
import { REGISTRABLE_ROLES } from '../types/auth'
import { problemFrom } from '../lib/api'
import { resolveProductImage } from '../lib/productImages'
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
  const [selectedImage, setSelectedImage] = useState<string | null>(null)
  const [specTab, setSpecTab] = useState('packaging')
  const [quantity, setQuantity] = useState(1)
  const [adding, setAdding] = useState(false)
  const [addMessage, setAddMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const { user } = useAuth()
  const { refresh: refreshCart } = useCart()
  const isBuyer = user?.roles.some((role) => (REGISTRABLE_ROLES as readonly string[]).includes(role)) ?? false

  useEffect(() => {
    if (!idOrSlug) return
    let cancelled = false
    setLoading(true)
    setError(null)
    getProduct(idOrSlug)
      .then((data) => {
        if (!cancelled) {
          setProduct(data)
          setSelectedImage(null)
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
  }, [idOrSlug])

  const activeImage = product
    ? selectedImage ?? resolveProductImage(product.slug, product.primaryImageUrl)
    : null

  // Default the spec tabs to whichever section this product actually has.
  useEffect(() => {
    if (!product) return
    setSpecTab(
      product.packagingOptions.length > 0
        ? 'packaging'
        : product.certifications.length > 0
          ? 'certifications'
          : 'terms',
    )
  }, [product])

  // Default the quantity to the listing's MOQ (the checkout never enforces it, but it is the
  // natural first order for a buyer). Re-arms only when the product changes.
  useEffect(() => {
    if (!product) return
    const suggested = Math.floor(product.minimumOrderQuantity)
    setQuantity(suggested > 1 ? suggested : 1)
  }, [product])

  async function handleAddToCart() {
    if (!product) return
    setAdding(true)
    setAddMessage(null)
    try {
      await addToCart({ productId: product.id, quantity })
      setAddMessage({ ok: true, text: 'Added to cart.' })
      void refreshCart()
    } catch (err) {
      setAddMessage({ ok: false, text: problemFrom(err).detail })
    } finally {
      setAdding(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to products
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
                <img
                  src={activeImage ?? '/assets/produce.svg'}
                  alt={product.name}
                  className="h-64 w-full rounded-lg border border-stone-100 object-cover"
                />
                {product.images.length > 1 ? (
                  <div className="mt-3 flex gap-2 overflow-x-auto pb-1">
                    {product.images.map((img) => (
                      <button
                        key={img.id}
                        type="button"
                        onClick={() => setSelectedImage(img.url)}
                        className={`h-16 w-24 shrink-0 overflow-hidden rounded-md border-2 transition ${
                          activeImage === img.url ? 'border-brand-500' : 'border-transparent hover:border-stone-300'
                        }`}
                      >
                        <img src={img.url} alt={img.altText ?? product.name} className="h-full w-full object-cover" />
                      </button>
                    ))}
                  </div>
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

              {product.packagingOptions.length > 0 || product.certifications.length > 0 ? (
                <Card className="mt-6">
                  <Tabs
                    items={[
                      ...(product.packagingOptions.length > 0
                        ? [
                            {
                              id: 'packaging',
                              label: 'Packaging',
                              icon: <PackageIcon className="h-4 w-4" />,
                              count: product.packagingOptions.length,
                            },
                          ]
                        : []),
                      ...(product.certifications.length > 0
                        ? [
                            {
                              id: 'certifications',
                              label: 'Certifications',
                              icon: <AwardIcon className="h-4 w-4" />,
                              count: product.certifications.length,
                            },
                          ]
                        : []),
                      ...(product.paymentTermsOffered.length > 0
                        ? [
                            {
                              id: 'terms',
                              label: 'Payment terms',
                              icon: <CoinsIcon className="h-4 w-4" />,
                              count: product.paymentTermsOffered.length,
                            },
                          ]
                        : []),
                    ]}
                    value={specTab}
                    onChange={setSpecTab}
                    className="-mt-1"
                  />

                  <TabPanel id="packaging" activeId={specTab}>
                    <ul className="flex flex-wrap gap-2">
                      {product.packagingOptions.map((option) => (
                        <li
                          key={option}
                          className="rounded-full bg-stone-100 px-3 py-1 text-sm text-stone-700"
                        >
                          {option}
                        </li>
                      ))}
                    </ul>
                  </TabPanel>

                  <TabPanel id="certifications" activeId={specTab}>
                    <ul className="flex flex-wrap gap-2">
                      {product.certifications.map((cert) => (
                        <li
                          key={cert}
                          className="rounded-full bg-brand-50 px-3 py-1 text-sm font-medium text-brand-800"
                        >
                          {cert}
                        </li>
                      ))}
                    </ul>
                  </TabPanel>

                  <TabPanel id="terms" activeId={specTab}>
                    <ul className="flex flex-wrap gap-2">
                      {product.paymentTermsOffered.map((term) => (
                        <li
                          key={term}
                          className="rounded-lg bg-stone-100 px-2.5 py-1 font-mono text-xs text-stone-700"
                        >
                          {term}
                        </li>
                      ))}
                    </ul>
                  </TabPanel>
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
              </Card>

              {product.sellsDomestically ? (
                <Card className="border-brand-200 bg-brand-50/40">
                  <div className="flex items-center justify-between gap-2">
                    <h2 className="text-lg font-semibold">Buy now — domestic</h2>
                    <span className="rounded-full bg-brand-600 px-2.5 py-1 text-xs font-semibold text-white">
                      Local delivery
                    </span>
                  </div>
                  <dl className="mt-4 grid grid-cols-2 gap-3 text-sm">
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-stone-500">Unit price</dt>
                      <dd className="font-semibold">
                        {product.currency}{' '}
                        {Number(product.price).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}{' '}
                        / {labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-xs uppercase tracking-wide text-stone-500">Delivery fee</dt>
                      <dd className="font-semibold">
                        {product.currency}{' '}
                        {Number(product.domesticDeliveryFee).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                      </dd>
                    </div>
                  </dl>
                  <p className="mt-3 text-xs text-stone-600">
                    Flat per-line delivery included at checkout. Up to{' '}
                    {Number(product.availableQuantity).toLocaleString()} available.
                  </p>

                  {!user ? (
                    <Link
                      to="/login"
                      className="mt-4 block rounded-lg bg-brand-600 px-4 py-2.5 text-center text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
                    >
                      Sign in to add to cart
                    </Link>
                  ) : !isBuyer ? (
                    <div className="mt-4 rounded-lg bg-white/70 p-3 text-xs leading-relaxed text-stone-600">
                      Buy-now is available to Buyer, Importer and Distributor accounts. Your account can
                      still trade through RFQs.
                    </div>
                  ) : (
                    <>
                      <label className="mt-4 block">
                        <span className="mb-1.5 block text-sm font-medium text-stone-700">Quantity</span>
                        <input
                          type="number"
                          min={1}
                          max={Math.floor(product.availableQuantity)}
                          value={quantity}
                          onChange={(e) => setQuantity(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
                          className="w-full rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                        />
                      </label>
                      <Button
                        className="mt-3 w-full justify-center"
                        disabled={adding || quantity < 1 || quantity > Math.floor(product.availableQuantity)}
                        onClick={() => void handleAddToCart()}
                      >
                        <CartIcon className="h-4 w-4" />
                        {adding ? 'Adding…' : 'Add to cart'}
                      </Button>
                    </>
                  )}
                  {addMessage ? (
                    <p
                      className={`mt-3 text-sm font-medium ${addMessage.ok ? 'text-emerald-700' : 'text-red-700'}`}
                    >
                      {addMessage.ok ? `${addMessage.text} ` : addMessage.text}
                      {addMessage.ok ? (
                        <Link to="/cart" className="font-semibold text-brand-700 underline">
                          View cart
                        </Link>
                      ) : null}
                    </p>
                  ) : null}
                </Card>
              ) : null}

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