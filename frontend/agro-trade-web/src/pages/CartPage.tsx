import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { Button, Card, EmptyState, ErrorAlert, Field, InfoBanner, Spinner, TextArea } from '../components/ui'
import { PlusIcon, TrashIcon } from '../components/icons'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { useCart } from '../lib/cart-context'
import { checkoutCart, getMyCart, removeCartItem, setCartItemQuantity } from '../lib/trade'
import { PRICE_UNITS } from '../types/trade'
import type { CartDto, CartItemDto, CheckoutResultDto } from '../types/trade'

function money(currency: string, amount: number) {
  return `${currency} ${Number(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function unitLabel(unit: number) {
  const base = PRICE_UNITS[unit] ?? `Unit ${unit}`
  return base.startsWith('Per ') ? base.replace('Per ', '') : base
}

interface SellerGroup {
  sellerId: string
  tradingName: string
  items: CartItemDto[]
  subtotal: number
  deliveryFee: number
  total: number
}

export default function CartPage() {
  const { user } = useAuth()
  const { refresh } = useCart()

  const [cart, setCart] = useState<CartDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<{ code?: string; detail?: string } | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [mutationError, setMutationError] = useState<{ code?: string; detail?: string } | null>(null)

  const [address, setAddress] = useState({
    line1: '',
    line2: '',
    city: '',
    state: '',
    country: user?.homeCountry && user.homeCountry.length === 2 ? user.homeCountry : '',
    postalCode: '',
  })
  const [notes, setNotes] = useState('')

  const [checkingOut, setCheckingOut] = useState(false)
  const [checkoutError, setCheckoutError] = useState<{ code?: string; detail?: string } | null>(null)
  const [checkoutResult, setCheckoutResult] = useState<CheckoutResultDto | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setLoadError(null)
    try {
      setCart(await getMyCart())
    } catch (err) {
      setLoadError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const sellerGroups: SellerGroup[] = useMemo(() => {
    if (!cart) return []
    const groups = new Map<string, CartItemDto[]>()
    for (const item of cart.items) {
      const list = groups.get(item.sellerBusinessId) ?? []
      list.push(item)
      groups.set(item.sellerBusinessId, list)
    }
    return [...groups.entries()].map(([sellerId, items]) => ({
      sellerId,
      tradingName: items[0].sellerTradingName,
      items,
      subtotal: items.reduce((sum, i) => sum + i.lineTotal, 0),
      deliveryFee: items.reduce((sum, i) => sum + i.deliveryFee, 0),
      total: items.reduce((sum, i) => sum + i.lineTotal + i.deliveryFee, 0),
    }))
  }, [cart])

  const grandTotal = useMemo(() => sellerGroups.reduce((sum, g) => sum + g.total, 0), [sellerGroups])
  const hasUnavailable = !!cart?.items.some((i) => !i.isAvailable)

  async function changeQuantity(productId: string, next: number) {
    if (next < 1) return
    setBusy(productId)
    setMutationError(null)
    try {
      setCart(await setCartItemQuantity(productId, { quantity: next }))
      void refresh()
    } catch (err) {
      setMutationError(problemFrom(err))
    } finally {
      setBusy(null)
    }
  }

  async function removeLine(productId: string) {
    setBusy(productId)
    setMutationError(null)
    try {
      setCart(await removeCartItem(productId))
      void refresh()
    } catch (err) {
      setMutationError(problemFrom(err))
    } finally {
      setBusy(null)
    }
  }

  async function placeOrder() {
    setCheckingOut(true)
    setCheckoutError(null)
    try {
      const result = await checkoutCart({
        destination: {
          line1: address.line1.trim(),
          line2: address.line2.trim() || undefined,
          city: address.city.trim(),
          state: address.state.trim() || undefined,
          country: address.country,
          postalCode: address.postalCode.trim() || undefined,
        },
        notes: notes.trim() || undefined,
      })
      setCheckoutResult(result)
      setCart(null)
      void refresh()
    } catch (err) {
      setCheckoutError(problemFrom(err))
    } finally {
      setCheckingOut(false)
    }
  }

  const canCheckout =
    !!cart &&
    cart.items.length > 0 &&
    !hasUnavailable &&
    address.line1.trim().length > 0 &&
    address.city.trim().length > 0 &&
    address.country.length === 2 &&
    !checkingOut

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to marketplace
        </Link>

        <h1 className="mt-4 text-3xl font-bold tracking-tight">Your cart</h1>

        {loadError ? (
          <div className="mt-6">
            <ErrorAlert title="Cart not available" detail={loadError.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : checkoutResult ? (
          <div className="mt-6">
            <Card className="bg-brand-50/40">
              <p className="font-display text-xl font-semibold text-brand-900">Order placed 🎉</p>
              <p className="mt-1 text-sm text-stone-600">
                Your cart was split into {checkoutResult.orders.length}{' '}
                {checkoutResult.orders.length === 1 ? 'order' : 'orders'} — one per seller — and stock
                has been reserved. Settlement and delivery are handled on each order.
              </p>
              <ul className="mt-5 space-y-3">
                {checkoutResult.orders.map((order) => (
                  <li key={order.orderId} className="rounded-xl border border-stone-200 bg-white p-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-mono text-xs text-stone-400">{order.orderNumber}</p>
                        <p className="truncate font-medium">{order.sellerTradingName}</p>
                        <p className="text-xs text-stone-500">
                          {order.itemCount} line{order.itemCount === 1 ? '' : 's'} · delivered to your address
                        </p>
                      </div>
                      <p className="font-display font-bold">{money(order.currency, order.total)}</p>
                    </div>
                    <Link
                      to={`/dashboard/orders/${order.orderId}`}
                      className="mt-3 inline-block text-sm font-semibold text-brand-700 hover:underline"
                    >
                      View order →
                    </Link>
                  </li>
                ))}
              </ul>
              <div className="mt-6 flex flex-wrap gap-3">
                <Link to="/dashboard/orders">
                  <Button variant="secondary">View all orders</Button>
                </Link>
                <Link to="/marketplace">
                  <Button>Continue shopping</Button>
                </Link>
              </div>
            </Card>
          </div>
        ) : !cart || cart.items.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="Your cart is empty"
              body="Domestic listings that offer buy-now appear with an Add to cart button. Head to the marketplace to start shopping."
              action={
                <Link to="/marketplace">
                  <Button>Browse listings</Button>
                </Link>
              }
            />
          </div>
        ) : (
          <div className="mt-6 grid gap-6 lg:grid-cols-5">
            <div className="space-y-6 lg:col-span-3">
              {mutationError ? <ErrorAlert title="Cart update failed" detail={mutationError.detail} /> : null}

              {sellerGroups.map((group) => (
                <Card key={group.sellerId}>
                  <div className="flex items-center justify-between gap-3">
                    <h2 className="text-lg font-semibold">{group.tradingName}</h2>
                    <span className="rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-700">
                      {group.items.length} line{group.items.length === 1 ? '' : 's'}
                    </span>
                  </div>

                  <ul className="mt-4 divide-y divide-stone-100">
                    {group.items.map((item) => (
                      <li key={item.productId} className="flex flex-wrap items-center justify-between gap-3 py-4">
                        <div className="min-w-0 max-w-md">
                          <Link
                            to={`/products/${item.productId}`}
                            className="block truncate font-medium text-stone-800 transition hover:text-brand-700"
                          >
                            {item.productName}
                          </Link>
                          <p className="mt-0.5 text-xs text-stone-500">
                            {money(item.currency, item.price)} / {unitLabel(item.priceUnit).toLowerCase()}
                            {item.deliveryFee > 0 ? ` · delivery ${money(item.currency, item.deliveryFee)}` : ''}
                          </p>
                          {!item.isAvailable ? (
                            <p className="mt-1 text-xs font-medium text-amber-700">
                              No longer available — remove the line to continue.
                            </p>
                          ) : null}
                        </div>

                        <div className="flex items-center gap-4">
                          <div
                            className={`flex h-9 items-center rounded-xl border border-stone-300 ${
                              item.isAvailable ? '' : 'opacity-50'
                            }`}
                          >
                            <button
                              type="button"
                              aria-label="Decrease quantity"
                              disabled={!item.isAvailable || busy === item.productId || item.quantity <= 1}
                              onClick={() => void changeQuantity(item.productId, item.quantity - 1)}
                              className="grid h-9 w-9 place-items-center rounded-l-xl text-stone-500 transition hover:bg-stone-100 disabled:opacity-40"
                            >
                              −
                            </button>
                            <span className="min-w-12 text-center text-sm font-semibold tabular-nums">
                              {Number(item.quantity).toLocaleString()}
                            </span>
                            <button
                              type="button"
                              aria-label="Increase quantity"
                              disabled={
                                !item.isAvailable ||
                                busy === item.productId ||
                                item.quantity >= Math.floor(item.availableQuantity)
                              }
                              onClick={() => void changeQuantity(item.productId, item.quantity + 1)}
                              className="grid h-9 w-9 place-items-center rounded-r-xl text-stone-500 transition hover:bg-stone-100 disabled:opacity-40"
                            >
                              <PlusIcon className="h-4 w-4" />
                            </button>
                          </div>
                          <p className="w-28 text-right text-sm font-semibold">
                            {money(item.currency, item.lineTotal)}
                          </p>
                          <button
                            type="button"
                            aria-label={`Remove ${item.productName} from cart`}
                            disabled={busy === item.productId}
                            onClick={() => void removeLine(item.productId)}
                            className="grid h-9 w-9 place-items-center rounded-xl text-stone-400 transition hover:bg-red-50 hover:text-red-600"
                          >
                            <TrashIcon className="h-4 w-4" />
                          </button>
                        </div>
                      </li>
                    ))}
                  </ul>

                  <dl className="mt-2 space-y-1.5 rounded-xl bg-stone-50 p-4 text-sm">
                    <div className="flex items-center justify-between">
                      <dt className="text-stone-600">Goods subtotal</dt>
                      <dd className="font-medium">{money(group.items[0].currency, group.subtotal)}</dd>
                    </div>
                    <div className="flex items-center justify-between">
                      <dt className="text-stone-600">Delivery fee</dt>
                      <dd className="font-medium">{money(group.items[0].currency, group.deliveryFee)}</dd>
                    </div>
                    <div className="flex items-center justify-between border-t border-stone-200 pt-1.5">
                      <dt className="font-semibold">Group total</dt>
                      <dd className="font-display font-bold">{money(group.items[0].currency, group.total)}</dd>
                    </div>
                  </dl>
                </Card>
              ))}
            </div>

            <div className="space-y-6 lg:col-span-2">
              <Card className="sticky top-6">
                <h2 className="text-lg font-semibold">Checkout</h2>
                <p className="mt-1 text-sm text-stone-500">
                  {sellerGroups.length} seller{sellerGroups.length === 1 ? '' : 's'} ·{' '}
                  {cart?.totalQuantity ?? 0} item{(cart?.totalQuantity ?? 0) === 1 ? '' : 's'}
                </p>
                <dl className="mt-4 space-y-1.5 text-sm">
                  <div className="flex items-center justify-between">
                    <dt className="text-stone-600">Goods subtotal</dt>
                    <dd className="font-medium">
                      {money(cart?.items[0]?.currency ?? 'USD', sellerGroups.reduce((s, g) => s + g.subtotal, 0))}
                    </dd>
                  </div>
                  <div className="flex items-center justify-between">
                    <dt className="text-stone-600">Delivery</dt>
                    <dd className="font-medium">
                      {money(cart?.items[0]?.currency ?? 'USD', sellerGroups.reduce((s, g) => s + g.deliveryFee, 0))}
                    </dd>
                  </div>
                  <div className="flex items-center justify-between border-t border-stone-200 pt-1.5">
                    <dt className="font-semibold">Total</dt>
                    <dd className="font-display text-lg font-bold">{money(cart?.items[0]?.currency ?? 'USD', grandTotal)}</dd>
                  </div>
                </dl>

                {hasUnavailable ? (
                  <InfoBanner tone="warning" title="Some lines are unavailable">
                    Remove the flagged lines before placing the order — checkout refuses listings that changed
                    since they were added.
                  </InfoBanner>
                ) : null}

                <div className="mt-5 space-y-4">
                  <CountrySelect
                    id="checkout-country"
                    label="Delivery country"
                    value={address.country}
                    onChange={(code) => setAddress((a) => ({ ...a, country: code }))}
                    required
                    hint="Domestic delivery only — this must be the same country as the sellers’ origin."
                  />
                  <Field
                    label="Address line 1"
                    id="checkout-line1"
                    value={address.line1}
                    onChange={(e) => setAddress((a) => ({ ...a, line1: e.target.value }))}
                    placeholder="Street, building, area"
                    required
                  />
                  <Field
                    label="Address line 2 (optional)"
                    id="checkout-line2"
                    value={address.line2}
                    onChange={(e) => setAddress((a) => ({ ...a, line2: e.target.value }))}
                    placeholder="Suite, floor, landmark"
                  />
                  <div className="grid grid-cols-2 gap-3">
                    <Field
                      label="City"
                      id="checkout-city"
                      value={address.city}
                      onChange={(e) => setAddress((a) => ({ ...a, city: e.target.value }))}
                      placeholder="City / town"
                      required
                    />
                    <Field
                      label="State (optional)"
                      id="checkout-state"
                      value={address.state}
                      onChange={(e) => setAddress((a) => ({ ...a, state: e.target.value }))}
                      placeholder="State / region"
                    />
                  </div>
                  <Field
                    label="Postal code (optional)"
                    id="checkout-postal"
                    value={address.postalCode}
                    onChange={(e) => setAddress((a) => ({ ...a, postalCode: e.target.value }))}
                    placeholder="Postal / ZIP code"
                  />
                  <TextArea
                    label="Notes for the sellers (optional)"
                    id="checkout-notes"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    placeholder="Anything the sellers should know"
                  />
                </div>

                {checkoutError ? (
                  <div className="mt-4">
                    <ErrorAlert title="Checkout failed" detail={checkoutError.detail} />
                  </div>
                ) : null}

                <Button className="mt-5 w-full justify-center" disabled={!canCheckout} onClick={() => void placeOrder()}>
                  {checkingOut ? 'Placing order…' : `Place order · ${money(cart?.items[0]?.currency ?? 'USD', grandTotal)}`}
                </Button>
                <p className="mt-2 text-center text-xs text-stone-500">
                  Splits into one order per seller. Stock for the whole cart is reserved at once.
                </p>
              </Card>
            </div>
          </div>
        )}
      </main>
    </div>
  )
}