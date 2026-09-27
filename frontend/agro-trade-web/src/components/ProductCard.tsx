import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { ProductCardDto } from '../types/trade'
import { PRICE_UNITS, labelOf } from '../types/trade'
import { IncotermBadge, VerifiedBadge } from './badges'
import { productImageFor, resolveProductImage } from '../lib/productImages'

export function ProductCard({ product }: { product: ProductCardDto }) {
  const unit = labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()
  const priceLabel = `${product.currency} ${Number(product.price).toLocaleString()} / ${unit}`
  const fallbackImage = productImageFor(product.slug) ?? '/assets/produce.svg'
  const [imageSrc, setImageSrc] = useState(() => resolveProductImage(product.slug, product.primaryImageUrl))
  return (
    <Link
      to={`/products/${product.slug || product.id}`}
      className="group flex flex-col rounded-xl border border-stone-200 bg-white p-5 shadow-sm transition hover:border-brand-300 hover:shadow"
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="font-semibold group-hover:text-brand-700">{product.name}</h3>
        <VerifiedBadge visible={product.isSellerVerifiedBadgeVisible} />
      </div>

      <img
        src={imageSrc}
        onError={() => setImageSrc((prev) => (prev === fallbackImage ? prev : fallbackImage))}
        alt={product.name}
        className="mt-3 h-36 w-full rounded-lg border border-stone-100 object-cover"
      />

      <dl className="mt-4 grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
        <div className="col-span-2">
          <dt className="text-xs text-stone-500">Price</dt>
          <dd className="font-semibold text-brand-700">{priceLabel}</dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Available</dt>
          <dd className="font-medium">{Number(product.availableQuantity).toLocaleString()} {unit}</dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">MOQ</dt>
          <dd className="font-medium">{Number(product.minimumOrderQuantity).toLocaleString()} {unit}</dd>
        </div>
        <div className="col-span-2">
          <dt className="text-xs text-stone-500">Origin</dt>
          <dd className="font-medium">{product.originCountry}{product.portOfLoading ? ` · ${product.portOfLoading}` : ''}</dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Incoterm</dt>
          <dd>
            <IncotermBadge value={product.incoterms} />
            {product.incoterms === null || product.incoterms === undefined ? '—' : null}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-stone-500">Lead time</dt>
          <dd className="font-medium">{product.leadTimeDays ? `${product.leadTimeDays} days` : '—'}</dd>
        </div>
      </dl>

      <p className="mt-4 border-t border-stone-100 pt-3 text-sm text-stone-600">
        {product.sellerTradingName}
      </p>
    </Link>
  )
}