import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ListingPill, VerifiedBadge } from '../components/badges'
import { Button, Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { ConfirmDialog, Tooltip } from '../components/overlays'
import { deleteProduct, getMyBusinesses, searchProducts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { COMPANY_ROLE } from '../types/auth'
import { PRICE_UNITS, labelOf } from '../types/trade'
import { BuildingIcon, PencilIcon, PlusIcon, TrashIcon } from '../components/icons'
import type { BusinessSummaryDto, ProductCardDto } from '../types/trade'

export default function MyProductsPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const isCompany = (user?.roles ?? []).includes(COMPANY_ROLE)
  const [products, setProducts] = useState<ProductCardDto[] | null>(null)
  const [businesses, setBusinesses] = useState<BusinessSummaryDto[]>([])
  const [businessId, setBusinessId] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<ProductCardDto | null>(null)

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
      setPendingDelete(null)
      await load()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Company"
          title="My listings"
          subtitle="Manage the platform's supply."
          action={
            isCompany ? (
              <Link
                to="/products/new"
                className="inline-flex h-10 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700"
              >
                <PlusIcon className="h-4 w-4" />
                New listing
              </Link>
            ) : undefined
          }
        />

        {!isCompany ? (
          <div className="mt-6">
            <EmptyState
              title="Supplier listings are managed by the platform"
              body="Sign up and trade as a buyer/importer — publish RFQs to source imports. Only the Achievers Global Consult account manages export listings."
            />
          </div>
        ) : (
          <>
        {businesses.length > 1 ? (
          <div className="mt-6 flex max-w-sm items-center justify-between gap-3">
            <select
              value={businessId}
              onChange={(e) => setBusinessId(e.target.value)}
              className="w-full rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft outline-none transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
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
              body="Create your first listing — it will stay Draft until your business is verified."
            />
          </div>
        ) : (
          <ul className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((product) => (
              <li key={product.id}>
                <Card className="flex h-full flex-col">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <Link
                        to={`/products/${product.slug || product.id}`}
                        className="font-semibold text-stone-900 transition hover:text-brand-700"
                      >
                        {product.name}
                      </Link>
                      <p className="mt-1 flex flex-wrap items-baseline gap-x-1.5 text-sm">
                        <span className="font-display font-bold text-brand-700">
                          {product.currency} {Number(product.price).toLocaleString()}
                        </span>
                        <span className="text-stone-500">
                          / {labelOf(PRICE_UNITS, product.priceUnit).replace('Per ', '').toLowerCase()}
                        </span>
                      </p>
                    </div>
                    <div className="flex shrink-0 flex-col items-end gap-1">
                      {product.sellsDomestically ? (
                        <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold tracking-wide text-emerald-700 ring-1 ring-emerald-200 uppercase">
                          Domestic
                        </span>
                      ) : null}
                      <ListingPill status={product.listingStatus} />
                      <VerifiedBadge visible={product.isSellerVerifiedBadgeVisible} />
                    </div>
                  </div>
                  <p className="mt-3 flex items-center gap-1.5 text-sm text-stone-500">
                    <BuildingIcon className="h-3.5 w-3.5 shrink-0 text-stone-400" />
                    <span className="truncate">
                      {product.originCountry}
                      {product.portOfLoading ? ` · ${product.portOfLoading}` : ''} ·{' '}
                      {product.sellerTradingName}
                    </span>
                  </p>
                  <div className="mt-4 flex flex-1 items-end gap-2">
                    <Button
                      variant="secondary"
                      className="flex-1"
                      onClick={() => navigate(`/products/${product.slug || product.id}/edit`)}
                    >
                      <PencilIcon className="h-3.5 w-3.5" />
                      Edit
                    </Button>
                    <Tooltip label="Remove this listing from the marketplace">
                      <Button variant="danger" onClick={() => setPendingDelete(product)}>
                        <TrashIcon className="h-3.5 w-3.5" />
                        Delete
                      </Button>
                    </Tooltip>
                  </div>
                </Card>
              </li>
            ))}
          </ul>
        )}
          </>
        )}
      </main>

      <ConfirmDialog
        open={pendingDelete !== null}
        pending={deletingId !== null}
        onCancel={() => setPendingDelete(null)}
        onConfirm={() => {
          if (pendingDelete) void handleDelete(pendingDelete.id)
        }}
        title="Delete this listing?"
        confirmLabel="Delete listing"
        message={
          <>
            <strong className="font-semibold text-stone-800">{pendingDelete?.name}</strong> will be
            removed from the marketplace and buyers will no longer be able to find or request it.
            Existing quotations and conversations are kept.
          </>
        }
      />
    </div>
  )
}