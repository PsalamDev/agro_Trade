import { useState } from 'react'
import { Button, ErrorAlert } from './ui'
import { OrderPaymentPill } from './badges'
import { initiatePayment, settleSandboxPayment } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { ORDER_STATUS, PAYMENT_METHODS, PAYMENT_PROVIDERS, PAYMENT_STATUS } from '../types/trade'
import type { OrderDetailDto, PaymentDto } from '../types/trade'

function formatDate(value: string | null) {
  if (!value) return null
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

function money(amount: number, currency: string) {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency,
    maximumFractionDigits: 2,
  }).format(amount)
}

interface Props {
  order: OrderDetailDto
  payments: PaymentDto[]
  onChanged: () => Promise<void> | void
}

/**
 * Payments on one order (task 2.4).
 *
 * The buyer pays, so the pay button is theirs alone; the seller sees the same list read-only,
 * because knowing they have been paid is the commercially important half of the event.
 *
 * Settlement is deliberately not something this screen can do on its own. A charge starts
 * pending and only becomes paid when the gateway says so, which in the sandbox means a webhook.
 * The "Simulate" control stands in for the payer's checkout page, and drives the real signed
 * event through the real handler rather than flipping a status directly — otherwise the screen
 * would prove nothing about the path that actually matters.
 */
export function OrderPaymentsPanel({ order, payments, onChanged }: Props) {
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const settled = payments.filter((p) => p.status === PAYMENT_STATUS.Paid).reduce((sum, p) => sum + p.amount, 0)
  const outstanding = Math.max(0, order.total - settled)
  const inFlight = payments.some((p) => p.status === PAYMENT_STATUS.Pending)

  const isClosed = order.status === ORDER_STATUS.Cancelled || order.status === ORDER_STATUS.Completed
  const canPay = order.isBuyer && !isClosed && outstanding > 0 && !inFlight

  async function handlePay() {
    setBusyId('new')
    setError(null)
    try {
      // The key is generated once per click. If this request times out the user retries, which
      // produces a *new* key and therefore a second charge — so the guard only helps a client
      // that reuses the key, which the API layer does for a genuine retry of the same call.
      await initiatePayment(order.id, { idempotencyKey: crypto.randomUUID() })
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusyId(null)
    }
  }

  async function handleSimulate(paymentId: string, outcome: 'succeeded' | 'failed') {
    setBusyId(paymentId)
    setError(null)
    try {
      await settleSandboxPayment(paymentId, outcome)
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section className="mt-8">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Payments</h2>
        <p className="text-xs text-stone-500">
          {settled > 0 ? `${money(settled, order.currency)} of ${money(order.total, order.currency)} received` : 'Nothing received yet'}
        </p>
      </div>
      <p className="mt-1 text-sm text-stone-500">
        {order.currency} {money(order.total, order.currency)} total for this order.
        {outstanding > 0 ? ` ${money(outstanding, order.currency)} outstanding.` : ' Paid in full.'}
      </p>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'CORRIDOR_UNSUPPORTED'
                ? `No payment gateway settles ${order.currency} yet, so this order cannot be paid online. Record it as a bank transfer instead.`
                : error.code === 'ORDER_CLOSED'
                  ? 'This order is closed, so it cannot take a payment.'
                  : error.code === 'NOTHING_OUTSTANDING'
                    ? 'This order is already paid in full.'
                    : error.detail
            }
          />
        </div>
      ) : null}

      {payments.length === 0 ? (
        <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-6 text-center text-sm text-stone-500">
          No payments raised against this order yet.
        </p>
      ) : (
        <ul className="mt-3 divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
          {payments.map((payment) => {
            const isPending = payment.status === PAYMENT_STATUS.Pending
            const isSandbox = payment.provider === 1

            return (
              <li key={payment.id} className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="font-medium">{money(payment.amount, payment.currency)}</p>
                    <OrderPaymentPill status={payment.status} />
                  </div>
                  <p className="mt-0.5 text-sm text-stone-500">
                    {payment.reference} · {PAYMENT_METHODS[payment.method] ?? 'Unknown method'}
                    {' · '}
                    {PAYMENT_PROVIDERS[payment.provider] ?? 'Unknown provider'}
                  </p>
                  <p className="mt-0.5 text-xs text-stone-400">
                    {[
                      `Raised ${formatDate(payment.initiatedAtUtc)}`,
                      payment.paidAtUtc ? `Received ${formatDate(payment.paidAtUtc)}` : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </p>
                  {payment.notes ? <p className="mt-1 text-xs text-stone-500">{payment.notes}</p> : null}
                  {payment.failureReason ? (
                    <p className="mt-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-800">
                      {payment.failureReason}
                    </p>
                  ) : null}
                </div>

                {/* Stands in for the payer's checkout page. Only meaningful while the charge is
                    genuinely in flight, which is why it is hidden once it has settled. */}
                {isSandbox && isPending ? (
                  <div className="flex shrink-0 items-center gap-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={busyId !== null}
                      onClick={() => void handleSimulate(payment.id, 'failed')}
                    >
                      Decline
                    </Button>
                    <Button
                      size="sm"
                      disabled={busyId !== null}
                      onClick={() => void handleSimulate(payment.id, 'succeeded')}
                    >
                      {busyId === payment.id ? 'Settling…' : 'Simulate payment'}
                    </Button>
                  </div>
                ) : null}
              </li>
            )
          })}
        </ul>
      )}

      {canPay ? (
        <div className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
          <h3 className="text-sm font-semibold text-stone-700">Pay this order</h3>
          <p className="mt-1 text-xs text-stone-500">
            Raises a card charge for the {money(outstanding, order.currency)} outstanding and takes
            you to a checkout page. The order only counts as paid once the gateway confirms it.
          </p>
          <div className="mt-3">
            <Button disabled={busyId !== null} onClick={() => void handlePay()}>
              {busyId === 'new' ? 'Raising charge…' : `Pay ${money(outstanding, order.currency)}`}
            </Button>
          </div>
        </div>
      ) : order.isBuyer && inFlight ? (
        <p className="mt-5 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
          A charge is already in flight. Wait for the gateway to confirm it before raising another.
        </p>
      ) : order.isBuyer && outstanding <= 0 && payments.length > 0 ? (
        <p className="mt-5 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
          This order is paid in full. Nothing further is due.
        </p>
      ) : isClosed ? (
        <p className="mt-5 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
          This order is {order.status === ORDER_STATUS.Cancelled ? 'cancelled' : 'complete'}, so it is
          closed to new payments.
        </p>
      ) : null}
    </section>
  )
}
