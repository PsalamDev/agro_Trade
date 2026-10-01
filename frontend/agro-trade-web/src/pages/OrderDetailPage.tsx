import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { OrderPaymentPill, OrderStatusPill } from '../components/badges'
import { OrderDocumentsPanel } from '../components/OrderDocumentsPanel'
import { OrderPaymentsPanel } from '../components/OrderPaymentsPanel'
import { ConfirmDialog } from '../components/overlays'
import { Button, Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { getOrder, transitionOrder } from '../lib/trade'
import { ORDER_STATUS, PRICE_UNITS, PAYMENT_TERMS, SHIPMENT_MODES, labelOf, orderActions } from '../types/trade'
import type { OrderDetailDto, OrderParty } from '../types/trade'

function FieldRow({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">{label}</dt>
      <dd className="mt-0.5 text-sm font-medium">{value}</dd>
    </div>
  )
}

function money(currency: string, amount: number) {
  return `${currency} ${Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function quantityUnit(q: number, unit: number) {
  const base = PRICE_UNITS[unit] ?? `Unit ${unit}`
  const short = base.startsWith('Per ') ? base.replace('Per ', '') : base
  return `${Number(q).toLocaleString(undefined, { minimumFractionDigits: q % 1 === 0 ? 0 : 2, maximumFractionDigits: 2 })} ${short.toLowerCase()}`
}

function formatDate(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

export default function OrderDetailPage() {
  const { id } = useParams()
  const { user } = useAuth()

  const [order, setOrder] = useState<OrderDetailDto | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [loading, setLoading] = useState(true)
  const [actingTo, setActingTo] = useState<number | null>(null)
  const [actionError, setActionError] = useState<{ code?: string; detail?: string } | null>(null)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState('')

  // The server resolves which side this user is on from actual business ownership, so the page
  // no longer has to fetch the user's businesses to work it out — which also means a Company
  // user who owns a different business is correctly treated as an outsider here.
  const party: OrderParty = useMemo(() => {
    if (!order) return 'outsider'
    if (order.isBuyer) return 'buyer'
    if (order.isSeller) return 'seller'
    return 'outsider'
  }, [order])

  const load = useCallback(async () => {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      setOrder(await getOrder(id))
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [id])

  /** Reload without the spinner, for after an upload or a withdrawal. */
  const refresh = useCallback(async () => {
    if (!id) return
    try {
      setOrder(await getOrder(id))
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [id])

  useEffect(() => {
    void load()
  }, [load])

  const actions = useMemo(
    () => (order ? orderActions(order.status, party) : []),
    [order, party],
  )

  async function doTransition(to: number, reason?: string) {
    if (!id || !order) return
    setActingTo(to)
    setActionError(null)
    try {
      const updated = await transitionOrder(id, { to, reason: reason?.trim() || undefined })
      setOrder(updated)
      setCancelOpen(false)
      setCancelReason('')
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setActingTo(null)
    }
  }

  async function handleCancelConfirm() {
    await doTransition(ORDER_STATUS.Cancelled, cancelReason)
  }

  const canCancel = actions.some((a) => a.to === ORDER_STATUS.Cancelled)

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/dashboard/orders" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to orders
        </Link>

        {error ? (
          <div className="mt-6">
            <ErrorAlert
              title="Order not available"
              detail={
                error.code === 'ORDER_NOT_FOUND'
                  ? 'This order exists only for the buyer and the selling business.'
                  : error.detail
              }
            />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : order ? (
          <div className="mt-6 grid gap-6 lg:grid-cols-5">
            <div className="space-y-6 lg:col-span-3">
              <Card>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="font-mono text-xs text-stone-400">{order.orderNumber}</p>
                    <h1 className="mt-1 text-2xl font-bold tracking-tight">{order.sellerTradingName}</h1>
                    <p className="mt-1 text-sm text-stone-500">
                      {party === 'buyer' ? 'Purchased by you' : party === 'seller' ? 'Selling to buyer' : 'Order'}
                      {order.buyerDisplayName ? ` · ${order.buyerDisplayName}` : ''}
                    </p>
                  </div>
                  <div className="flex shrink-0 flex-wrap items-center gap-2">
                    <OrderStatusPill status={order.status} />
                    <OrderPaymentPill status={order.paymentStatus} />
                  </div>
                </div>

                <dl className="mt-6 grid grid-cols-2 gap-4 rounded-xl bg-stone-50 p-4 sm:grid-cols-3">
                  <FieldRow label="Currency" value={order.currency} />
                  <FieldRow label="Incoterm" value={labelOf(['EXW', 'FCA', 'FAS', 'FOB', 'CFR', 'CIF', 'CPT', 'CIP', 'DAP', 'DPU', 'DDP'], order.incoterms)} />
                  <FieldRow label="Payment terms" value={labelOf(PAYMENT_TERMS, order.paymentTerms)} />
                  {order.portOfLoading ? <FieldRow label="Port of loading" value={order.portOfLoading} /> : null}
                  {order.portOfDischarge ? <FieldRow label="Port of discharge" value={order.portOfDischarge} /> : null}
                  {order.destinationCountry ? <FieldRow label="Destination" value={order.destinationCountry} /> : null}
                  {order.transportMode !== null && order.transportMode !== undefined ? (
                    <FieldRow label="Transport" value={labelOf(SHIPMENT_MODES, order.transportMode)} />
                  ) : null}
                  {order.placedAtUtc ? <FieldRow label="Placed" value={formatDate(order.placedAtUtc)} /> : null}
                  {order.completedAtUtc ? <FieldRow label="Completed" value={formatDate(order.completedAtUtc)} /> : null}
                  {order.expectedDeliveryUtc ? <FieldRow label="Expected delivery" value={formatDate(order.expectedDeliveryUtc)} /> : null}
                  <FieldRow label="Created" value={formatDate(order.createdAtUtc)} />
                  <FieldRow label="Updated" value={formatDate(order.updatedAtUtc)} />
                </dl>

                {order.notes ? (
                  <div className="mt-6">
                    <h2 className="text-lg font-semibold">Notes</h2>
                    <p className="mt-2 whitespace-pre-line text-sm leading-relaxed text-stone-600">{order.notes}</p>
                  </div>
                ) : null}

                <div className="mt-6">
                  <h2 className="text-lg font-semibold">Line items</h2>
                  {order.items.length === 0 ? (
                    <p className="mt-2 text-sm text-stone-500">This order has no line items.</p>
                  ) : (
                    <ul className="mt-3 divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                      {order.items.map((item) => (
                        <li key={item.id} className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
                          <div className="min-w-0">
                            <p className="truncate font-medium">{item.productNameSnapshot}</p>
                            {item.productSkuSnapshot ? (
                              <p className="text-xs text-stone-500">{item.productSkuSnapshot}</p>
                            ) : null}
                          </div>
                          <dl className="grid grid-cols-3 gap-4 text-right text-sm">
                            <div>
                              <dt className="text-xs uppercase tracking-wide text-stone-500">Qty</dt>
                              <dd className="font-medium">{quantityUnit(item.quantity, item.unit)}</dd>
                            </div>
                            <div>
                              <dt className="text-xs uppercase tracking-wide text-stone-500">Unit price</dt>
                              <dd className="font-medium">{money(item.currency, item.unitPrice)}</dd>
                            </div>
                            <div>
                              <dt className="text-xs uppercase tracking-wide text-stone-500">Line total</dt>
                              <dd className="font-semibold">{money(item.currency, item.lineTotal)}</dd>
                            </div>
                          </dl>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>

                <div className="mt-6">
                  <h2 className="text-lg font-semibold">Totals</h2>
                  <dl className="mt-3 space-y-2 rounded-xl border border-stone-200 p-4">
                    <div className="flex items-center justify-between">
                      <dt className="text-sm text-stone-600">Subtotal</dt>
                      <dd className="text-sm font-medium">{money(order.currency, order.subtotal)}</dd>
                    </div>
                    <div className="flex items-center justify-between">
                      <dt className="text-sm text-stone-600">Discount</dt>
                      <dd className="text-sm font-medium">{money(order.currency, order.discount)}</dd>
                    </div>
                    <div className="flex items-center justify-between border-t border-stone-100 pt-2">
                      <dt className="font-semibold">Total</dt>
                      <dd className="font-display text-lg font-bold">{money(order.currency, order.total)}</dd>
                    </div>
                    <div className="flex items-center justify-between">
                      <dt className="text-sm text-stone-600">Amount paid</dt>
                      <dd className="text-sm font-medium">{money(order.currency, order.amountPaid)}</dd>
                    </div>
                    <div className="flex items-center justify-between">
                      <dt className="text-sm text-stone-600">Balance due</dt>
                      <dd className="text-sm font-medium">{money(order.currency, order.balanceDue)}</dd>
                    </div>
                  </dl>
                </div>

                <div className="mt-6 grid gap-4 sm:grid-cols-2">
                  <Card>
                    <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Buyer</h3>
                    <p className="mt-1 font-medium">
                      {order.buyerDisplayName}
                      {order.isBuyer ? ' (you)' : ''}
                    </p>
                    <p className="mt-1 font-mono text-xs text-stone-400">{order.buyerUserId}</p>
                  </Card>
                  <Card>
                    <h3 className="text-sm font-semibold uppercase tracking-wide text-stone-500">Seller</h3>
                    <p className="mt-1 font-medium">
                      {order.sellerTradingName}
                      {order.isSeller ? ' (you)' : ''}
                    </p>
                    <p className="mt-1 font-mono text-xs text-stone-400">{order.sellerBusinessId}</p>
                  </Card>
                </div>

                <OrderPaymentsPanel order={order} payments={order.payments} onChanged={refresh} />

                <OrderDocumentsPanel
                  order={order}
                  documents={order.documents}
                  userId={user?.id ?? null}
                  onChanged={refresh}
                />
              </Card>
            </div>

            <div className="lg:col-span-2">
              <Card className="sticky top-6">
                <h2 className="text-lg font-semibold">Actions</h2>
                <p className="mt-1 text-sm text-stone-500">
                  {party === 'buyer' ? 'Buyer actions' : party === 'seller' ? 'Seller actions' : 'No actions available'}
                </p>

                {actionError ? (
                  <div className="mt-3">
                    <ErrorAlert detail={actionError.detail} />
                  </div>
                ) : null}

                {actions.length === 0 ? (
                  <EmptyState
                    title="No actions available right now"
                    body={party === 'outsider' ? 'Only the buyer and selling business can act on this order.' : 'This order is in a final state.'}
                  />
                ) : (
                  <div className="mt-3 space-y-2">
                    {actions.map((action) => (
                      <Button
                        key={action.to}
                        variant={action.destructive ? 'danger' : 'primary'}
                        className="w-full justify-center"
                        disabled={actingTo !== null}
                        onClick={() =>
                          action.to === ORDER_STATUS.Cancelled ? setCancelOpen(true) : void doTransition(action.to)
                        }
                      >
                        {actingTo === action.to ? 'Updating…' : action.label}
                      </Button>
                    ))}
                  </div>
                )}

                {canCancel ? (
                  <ConfirmDialog
                    open={cancelOpen}
                    onCancel={() => setCancelOpen(false)}
                    onConfirm={handleCancelConfirm}
                    title="Cancel this order?"
                    message={
                      <>
                        Cancelling releases any stock reserved for this order. The other party will be notified.
                        <label className="mt-3 block">
                          <span className="mb-1.5 block text-xs font-medium text-stone-700">Reason (optional)</span>
                          <textarea
                            value={cancelReason}
                            onChange={(e) => setCancelReason(e.target.value)}
                            rows={3}
                            placeholder="Why is this order being cancelled?"
                            className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                          />
                        </label>
                      </>
                    }
                    confirmLabel="Cancel order"
                    tone="danger"
                    pending={actingTo === ORDER_STATUS.Cancelled}
                  />
                ) : null}
              </Card>
            </div>
          </div>
        ) : null}
      </main>
    </div>
  )
}