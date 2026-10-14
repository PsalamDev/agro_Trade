import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { IncotermBadge, OrderPaymentPill, OrderStatusPill } from '../components/badges'
import { Tabs } from '../components/overlays'
import { EmptyState, ErrorAlert, PageHeader, Skeleton } from '../components/ui'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { getMyOrders, getSellingOrders } from '../lib/trade'
import { formatUtcDate } from '../lib/format'
import { COMPANY_ROLE } from '../types/auth'
import { ORDER_STATUS, ORDER_STATUSES } from '../types/trade'
import type { OrderCardDto } from '../types/trade'

/**
 * Status filters, excluding Draft: an order is placed on creation, so Draft is a state the
 * UI never puts in front of anyone. "All" is the empty string.
 */
const STATUS_FILTERS = ORDER_STATUSES.map((_, value) => String(value))
  .filter((value) => value !== String(ORDER_STATUS.Draft))
  .map((value) => ({ value, label: ORDER_STATUSES[Number(value)] }))

function money(currency: string, amount: number) {
  return `${currency} ${Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`rounded-full px-3 py-1.5 text-xs font-semibold transition duration-200 ease-soft ${
        active
          ? 'bg-brand-600 text-white shadow-brand'
          : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50 hover:text-stone-900'
      }`}
    >
      {children}
    </button>
  )
}

function OrderRow({ order, view }: { order: OrderCardDto; view: 'buyer' | 'seller' }) {
  // Each side sees the other party named, since its own name is redundant on the row.
  const counterparty = view === 'buyer' ? order.sellerTradingName : order.buyerDisplayName

  return (
    <li>
      <Link
        to={`/dashboard/orders/${order.id}`}
        className="block rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft hover:-translate-y-0.5 hover:border-brand-200 hover:shadow-lift"
      >
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="font-mono text-xs text-stone-400">{order.orderNumber}</p>
            <p className="mt-1 truncate font-semibold text-stone-900">{counterparty}</p>
          </div>
          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <OrderStatusPill status={order.status} />
            <OrderPaymentPill status={order.paymentStatus} />
          </div>
        </div>

        <dl className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <div>
            <dt className="text-xs font-medium tracking-wide text-stone-500 uppercase">Total</dt>
            <dd className="mt-0.5 font-display text-base font-bold text-stone-900">
              {money(order.currency, order.total)}
            </dd>
            {order.deliveryFee > 0 ? (
              <dd className="mt-0.5 text-xs text-stone-500">incl. {money(order.currency, order.deliveryFee)} delivery</dd>
            ) : null}
          </div>
          <div>
            <dt className="text-xs font-medium tracking-wide text-stone-500 uppercase">Incoterm</dt>
            <dd className="mt-0.5">
              <IncotermBadge value={order.incoterms} />
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium tracking-wide text-stone-500 uppercase">Route</dt>
            <dd className="mt-0.5 truncate text-sm font-medium">
              {order.deliveryFee > 0
                ? `Domestic${order.destinationCountry ? ` · ${order.destinationCountry}` : ''}`
                : `${order.portOfLoading ?? '—'} → ${order.portOfDischarge ?? '—'}`}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium tracking-wide text-stone-500 uppercase">
              {order.placedAtUtc ? 'Placed' : 'Created'}
            </dt>
            <dd className="mt-0.5 text-sm font-medium">{formatUtcDate(order.placedAtUtc ?? order.createdAtUtc)}</dd>
          </div>
        </dl>

        <p className="mt-3 text-xs text-stone-500">
          {order.itemCount} line{order.itemCount === 1 ? '' : 's'}
          {order.expectedDeliveryUtc ? ` · expected ${formatUtcDate(order.expectedDeliveryUtc)}` : ''}
        </p>
      </Link>
    </li>
  )
}

/** Content-shaped placeholder for a result row, so loading reads as the list arriving. */
function OrderRowSkeleton() {
  return (
    <li className="rounded-2xl border border-stone-200 bg-white p-5 shadow-card">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="mt-2.5 h-4 w-44" />
        </div>
        <div className="flex shrink-0 gap-2">
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-5 w-24 rounded-full" />
        </div>
      </div>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-9" />
        ))}
      </div>
    </li>
  )
}

export default function OrdersListPage() {
  const { user } = useAuth()
  const isCompany = (user?.roles ?? []).includes(COMPANY_ROLE)

  const [view, setView] = useState<'buyer' | 'seller'>(isCompany ? 'seller' : 'buyer')
  const [status, setStatus] = useState('')
  const [orders, setOrders] = useState<OrderCardDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    setOrders(null)
    try {
      const params = { status: status === '' ? undefined : Number(status), pageSize: 100 }
      const result = view === 'buyer' ? await getMyOrders(params) : await getSellingOrders(params)
      setOrders(result.items)
    } catch (err) {
      setError(problemFrom(err))
      setOrders([])
    }
  }, [view, status])

  useEffect(() => {
    void load()
  }, [load])

  const tabs = [
    { id: 'buyer', label: 'My purchases' },
    ...(isCompany ? [{ id: 'seller', label: 'Orders to fulfil' }] : []),
  ]

  const isBuyer = view === 'buyer'

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow={isBuyer ? 'Procurement' : 'Fulfilment'}
          title="Orders"
          subtitle={
            isBuyer
              ? 'Orders you have placed, from placement through to receipt.'
              : 'Orders buyers have placed against your business, and what each one needs next.'
          }
          action={
            isBuyer ? (
              <Link
                to="/rfqs/new"
                className="inline-flex h-10 shrink-0 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700"
              >
                Publish an RFQ
              </Link>
            ) : undefined
          }
        />

        {tabs.length > 1 ? (
          <div className="mt-6">
            <Tabs items={tabs} value={view} onChange={(id) => setView(id as 'buyer' | 'seller')} />
          </div>
        ) : null}

        <div className="mt-5 flex flex-wrap items-center gap-2">
          <FilterChip active={status === ''} onClick={() => setStatus('')}>
            All
          </FilterChip>
          {STATUS_FILTERS.map((filter) => (
            <FilterChip
              key={filter.value}
              active={status === filter.value}
              onClick={() => setStatus(filter.value)}
            >
              {filter.label}
            </FilterChip>
          ))}
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load orders" detail={error.detail} />
          </div>
        ) : null}

        {orders === null ? (
          <ul aria-busy="true" aria-label="Loading orders" className="mt-6 space-y-4">
            {Array.from({ length: 4 }, (_, i) => (
              <OrderRowSkeleton key={i} />
            ))}
          </ul>
        ) : orders.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title={status ? 'No orders with that status' : 'No orders yet'}
              body={
                status
                  ? 'Clear the status filter to see the rest of your orders.'
                  : isBuyer
                    ? 'Orders appear here once you accept a quotation. Publish an RFQ to start sourcing.'
                    : 'Orders appear here when a buyer places one against your business.'
              }
              action={
                status ? (
                  <button
                    type="button"
                    onClick={() => setStatus('')}
                    className="rounded-lg bg-stone-100 px-4 py-2 text-sm font-semibold text-stone-700 transition hover:bg-stone-200"
                  >
                    Clear filter
                  </button>
                ) : isBuyer ? (
                  <Link
                    to="/rfqs/new"
                    className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-brand-700"
                  >
                    Publish an RFQ
                  </Link>
                ) : undefined
              }
            />
          </div>
        ) : (
          <>
            <ul className="mt-6 space-y-4">
              {orders.map((order) => (
                <OrderRow key={order.id} order={order} view={view} />
              ))}
            </ul>
            <p className="mt-4 text-xs text-stone-500">
              Showing {orders.length} order{orders.length === 1 ? '' : 's'}
              {status ? ' with this status' : ''}.
            </p>
          </>
        )}
      </main>
    </div>
  )
}
