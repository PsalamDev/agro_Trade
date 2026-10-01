import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { IncotermBadge, OrderPaymentPill, OrderStatusPill } from '../components/badges'
import { Tabs } from '../components/overlays'
import { EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { getMyOrders, getSellingOrders } from '../lib/trade'
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

function formatDate(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

function money(currency: string, amount: number) {
  return `${currency} ${Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
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
              {order.portOfLoading ?? '—'} → {order.portOfDischarge ?? '—'}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium tracking-wide text-stone-500 uppercase">
              {order.placedAtUtc ? 'Placed' : 'Created'}
            </dt>
            <dd className="mt-0.5 text-sm font-medium">{formatDate(order.placedAtUtc ?? order.createdAtUtc)}</dd>
          </div>
        </dl>

        <p className="mt-3 text-xs text-stone-500">
          {order.itemCount} line{order.itemCount === 1 ? '' : 's'}
          {order.expectedDeliveryUtc ? ` · expected ${formatDate(order.expectedDeliveryUtc)}` : ''}
        </p>
      </Link>
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

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <h1 className="text-2xl font-bold tracking-tight">Orders</h1>
        <p className="mt-1 text-sm text-stone-500">
          {view === 'buyer'
            ? 'Orders you have placed, from placement through to receipt.'
            : 'Orders buyers have placed against your business, and what each one needs next.'}
        </p>

        {tabs.length > 1 ? (
          <div className="mt-6">
            <Tabs items={tabs} value={view} onChange={(id) => setView(id as 'buyer' | 'seller')} />
          </div>
        ) : null}

        <div className="mt-6 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => setStatus('')}
            className={`rounded-lg px-3 py-1.5 text-xs font-medium transition ${
              status === ''
                ? 'bg-brand-600 text-white'
                : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50'
            }`}
          >
            All
          </button>
          {STATUS_FILTERS.map((filter) => (
            <button
              key={filter.value}
              type="button"
              onClick={() => setStatus(filter.value)}
              className={`rounded-lg px-3 py-1.5 text-xs font-medium transition ${
                status === filter.value
                  ? 'bg-brand-600 text-white'
                  : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50'
              }`}
            >
              {filter.label}
            </button>
          ))}
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load orders" detail={error.detail} />
          </div>
        ) : null}

        {orders === null ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : orders.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title={status ? 'No orders with that status' : 'No orders yet'}
              body={
                status
                  ? 'Clear the status filter to see the rest of your orders.'
                  : view === 'buyer'
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
                ) : view === 'buyer' ? (
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
            <div className="mt-6 space-y-4">
              {orders.map((order) => (
                <OrderRow key={order.id} order={order} view={view} />
              ))}
            </div>
            <p className="mt-4 text-xs text-stone-500">
              {orders.length} order{orders.length === 1 ? '' : 's'}
            </p>
          </>
        )}
      </main>
    </div>
  )
}