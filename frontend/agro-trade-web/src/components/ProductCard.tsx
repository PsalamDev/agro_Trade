import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { ProductCardDto } from '../types/trade'
import { PRICE_UNITS, labelOf } from '../types/trade'
import { IncotermBadge, VerifiedBadge } from './badges'
import { Tooltip } from './overlays'
import { productImageFor, resolveProductImage } from '../lib/productImages'
import { BuildingIcon, ClockIcon, MapPinIcon, PackageIcon, ScaleIcon } from './icons'

export function ProductCard({ product }: { product: ProductCardDto }) {
  const unit = labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()
  const fallbackImage = productImageFor(product.slug) ?? '/assets/produce.svg'
  const [imageSrc, setImageSrc] = useState(() => resolveProductImage(product.slug, product.primaryImageUrl))

  const specs = [
    {
      label: 'Available',
      tip: 'Quantity currently in stock and ready to ship.',
      icon: <PackageIcon className="h-3.5 w-3.5" />,
      value: (
        <>
          {Number(product.availableQuantity).toLocaleString()}{' '}
          <span className="font-normal text-stone-500">{unit}</span>
        </>
      ),
    },
    {
      label: 'MOQ',
      tip: 'Minimum order quantity — the smallest lot this seller will ship.',
      icon: <ScaleIcon className="h-3.5 w-3.5" />,
      value: (
        <>
          {Number(product.minimumOrderQuantity).toLocaleString()}{' '}
          <span className="font-normal text-stone-500">{unit}</span>
        </>
      ),
    },
    {
      label: 'Lead time',
      tip: 'Working days from confirmed order to cargo ready at origin.',
      icon: <ClockIcon className="h-3.5 w-3.5" />,
      value: product.leadTimeDays ? `${product.leadTimeDays} days` : '—',
    },
    {
      label: 'Incoterm',
      tip: 'Trade term that fixes where responsibility and cost transfer.',
      icon: <PackageIcon className="h-3.5 w-3.5" />,
      value: <IncotermBadge value={product.incoterms} />,
    },
  ]

  return (
    <Link
      to={`/products/${product.slug || product.id}`}
      className="group flex flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
    >
      <div className="relative overflow-hidden bg-stone-100">
        <img
          src={imageSrc}
          onError={() => setImageSrc((prev) => (prev === fallbackImage ? prev : fallbackImage))}
          alt={product.name}
          className="h-44 w-full object-cover transition duration-700 ease-soft group-hover:scale-105"
        />
        {product.isSellerVerifiedBadgeVisible ? (
          <div className="absolute left-3 top-3">
            <VerifiedBadge visible size="lg" />
          </div>
        ) : null}
      </div>

      <div className="flex flex-1 flex-col p-5">
        <h3 className="font-display leading-snug font-semibold transition group-hover:text-brand-700">
          {product.name}
        </h3>

        <p className="mt-2.5 flex flex-wrap items-baseline gap-x-1.5">
          <span className="text-xs font-medium tracking-wide text-stone-500 uppercase">Price</span>
          <span className="font-display text-lg leading-none font-bold text-brand-700">
            {product.currency} {Number(product.price).toLocaleString()}
          </span>
          <span className="text-sm text-stone-500">/ {unit}</span>
        </p>

        <p className="mt-3 flex items-center gap-1.5 text-sm text-stone-600">
          <MapPinIcon className="h-3.5 w-3.5 shrink-0 text-stone-400" />
          {product.originCountry}
          {product.portOfLoading ? (
            <>
              <span className="text-stone-300">·</span>
              <span className="truncate">{product.portOfLoading}</span>
            </>
          ) : null}
        </p>

        <dl className="mt-4 grid grid-cols-2 gap-2">
          {specs.map((spec) => (
            <div key={spec.label} className="rounded-xl bg-stone-50 px-2.5 py-2">
              <dt className="flex items-center gap-1.5 text-[11px] font-medium tracking-wide text-stone-500 uppercase">
                <Tooltip label={spec.tip} placement="top">
                  <span className="flex items-center gap-1.5">
                    {spec.icon}
                    {spec.label}
                  </span>
                </Tooltip>
              </dt>
              <dd className="mt-0.5 text-sm font-semibold text-stone-800">{spec.value}</dd>
            </div>
          ))}
        </dl>

        <p className="mt-auto flex items-center gap-2 border-t border-stone-100 pt-4 text-sm font-medium text-stone-600">
          <BuildingIcon className="h-4 w-4 shrink-0 text-stone-400" />
          <span className="truncate">{product.sellerTradingName}</span>
        </p>
      </div>
    </Link>
  )
}
