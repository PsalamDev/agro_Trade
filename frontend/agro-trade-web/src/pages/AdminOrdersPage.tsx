import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { OrderPaymentPill, OrderStatusPill } from '../components/badges'
import { Modal } from '../components/overlays'
import { Button, Card, EmptyState, ErrorAlert, PageHeader, Spinner, TextArea } from '../components/ui'
import { PackageIcon } from '../components/icons'
import { adminOrderPayments, adminRefundPayment, adminSearchOrders } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc, money } from '../lib/format'
import { ORDER_STATUSES, PAYMENT_STATUSES, PAYMENT_STATUS } from '../types/trade'
import type { AdminOrderRowDto, PagedResult, PaymentDto } from '../types/trade'

/**
 * Queues for the statuses worth filtering by, plus "all".
 *
 * <p>
 * Draft and Cancelled are included because an administrator reconciling paperwork needs to find
 * the orders that never went anywhere — the states the parties themselves cannot see are exactly
 * the ones that go stale unnoticed.
 * </p>
 */
const STATUS_FILTERS = [
  { value: '', label: 'All' },
  ...ORDER_STATUSES.map((label, index) => ({ value: String(index), label })),
]

const PAYMENT_FILTERS = [
  { value: '', label: 'Any payment status' },
  ...PAYMENT_STATUSES.map((label, index) => ({ value: String(index), label })),
]

const controlClass =
  'w-full rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

export default function AdminOrdersPage() {
  const [status, setStatus] = useState('')
  const [paymentStatus, setPaymentStatus] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminOrderRowDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)
  const [refunding, setRefunding] = useState<AdminOrderRowDto | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchOrders({
          q: term.trim() || undefined,
          status: status === '' ? undefined : Number(status),
          paymentStatus: paymentStatus === '' ? undefined : Number(paymentStatus),
          page,
          pageSize: 25,
        }),
      )
    } catch (page) {
      setError(problemFrom(page))
    }
  }, [status, paymentStatus, term, page])

  useEffect(() => {
    void load()
  }, [load])

  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Orders"
          subtitle="Every order on the platform. Ordered value and settled value are shown separately because an order nobody has paid for is a different fact from one that has been paid in full — and the settled column nets out refunds, so it shows the money the seller actually still holds."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-xs">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search order number, party or country
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="AG-2026-000001"
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Payment</span>
            <select
              value={paymentStatus}
              onChange={(e) => applyFilter(setPaymentStatus)(e.target.value)}
              className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            >
              {PAYMENT_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          {STATUS_FILTERS.map((filter) => (
            <button
              key={filter.value}
              type="button"
              onClick={() => applyFilter(setStatus)(filter.value)}
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

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading orders…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching orders"
              body="An order appears once a quotation is accepted."
              icon={<PackageIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} order{result!.total === 1 ? '' : 's'}
              </p>

              {/*
                A table rather than the row-card list the review queues use: most of this queue is
                read, and the one thing acted on from here is a refund. Nothing else can be approved
                or rejected on this screen, so the useful shape is a scannable grid where the money
                columns line up down the page.
              */}
              <div className="overflow-x-auto rounded-xl border border-stone-200">
                <table className="w-full min-w-[52rem] text-left text-sm">
                  <thead className="bg-stone-50 text-xs text-stone-500">
                    <tr>
                      <th scope="col" className="px-4 py-2.5 font-medium">
                        Order
                      </th>
                      <th scope="col" className="px-4 py-2.5 font-medium">
                        Buyer
                      </th>
                      <th scope="col" className="px-4 py-2.5 font-medium">
                        Seller
                      </th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">
                        Ordered
                      </th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">
                        Paid
                      </th>
                      <th scope="col" className="px-4 py-2.5 font-medium">
                        Status
                      </th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">
                        Placed
                      </th>
                      <th scope="col" className="px-4 py-2.5 text-right font-medium">
                        Refund
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-stone-100 bg-white">
                    {result!.items.map((order) => (
                      <tr key={order.id}>
                        <td className="px-4 py-3">
                          <p className="font-mono text-xs font-semibold text-stone-800">
                            {order.orderNumber}
                          </p>
                          <p className="text-[11px] text-stone-400">
                            {order.destinationCountry ?? '—'}
                            {order.shipmentCount > 0 ? ` · ${order.shipmentCount} consignment` : ''}
                          </p>
                        </td>
                        <td className="px-4 py-3 text-stone-600">
                          {order.buyerDisplayName ?? '—'}
                        </td>
                        <td className="px-4 py-3 text-stone-600">
                          {order.sellerTradingName ?? '—'}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums text-stone-800">
                          {money(order.total, order.currency)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <span
                            className={
                              order.amountPaid >= order.total && order.total > 0
                                ? 'font-medium tabular-nums text-brand-700'
                                : 'tabular-nums text-stone-600'
                            }
                          >
                            {money(order.amountPaid, order.currency)}
                          </span>
                          {order.amountPaid < order.total && order.total > 0 ? (
                            <span className="block text-[11px] tabular-nums text-amber-700">
                              {money(order.total - order.amountPaid, order.currency)} due
                            </span>
                          ) : null}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <OrderStatusPill status={order.status} />
                            <OrderPaymentPill status={order.paymentStatus} />
                          </div>
                        </td>
                        <td className="px-4 py-3 text-right text-xs text-stone-500">
                          {formatUtc(order.placedAtUtc ?? order.createdAtUtc)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          {order.amountPaid > 0 ? (
                            <button
                              type="button"
                              onClick={() => setRefunding(order)}
                              className="rounded-lg bg-white px-2.5 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50"
                            >
                              Refund
                            </button>
                          ) : (
                            <span className="text-[11px] text-stone-300">—</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {result!.totalPages > 1 ? (
                <div className="mt-4 flex items-center justify-between text-sm">
                  <button
                    type="button"
                    disabled={page <= 1}
                    onClick={() => setPage((p) => p - 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    ← Previous
                  </button>
                  <span className="text-xs text-stone-500">
                    Page {page} of {result!.totalPages}
                  </span>
                  <button
                    type="button"
                    disabled={page >= result!.totalPages}
                    onClick={() => setPage((p) => p + 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    Next →
                  </button>
                </div>
              ) : null}
            </>
          )}
        </div>

        <Card className="mt-8 px-4 py-3.5">
          <p className="text-xs text-stone-500">
            Administrators can see every order on the platform and give money back on a settled
            payment, but cannot otherwise change one. Orders move through their lifecycle by the
            buyer's and seller's actions, and the{' '}
            <Link to="/admin/audit-log" className="font-medium text-brand-700 hover:underline">
              audit trail
            </Link>{' '}
            records the decisions — including every refund — an administrator makes elsewhere.
          </p>
        </Card>
      </main>

      {refunding ? (
        <RefundPaymentDialog
          order={refunding}
          onClose={() => setRefunding(null)}
          onRefunded={() => {
            setRefunding(null)
            void load()
          }}
        />
      ) : null}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Refund dialog                                                              */
/* -------------------------------------------------------------------------- */

interface RefundPaymentDialogProps {
  order: AdminOrderRowDto
  onClose: () => void
  onRefunded: () => void
}

/** How much of a payment is still refundable: what arrived, less what already went back. */
function refundable(payment: PaymentDto) {
  return payment.amount - (payment.refundedAmount ?? 0)
}

/**
 * The one money action an administrator takes from this page. Picks a settled payment on the
 * order, refunds all or part of it, and explains why — the reason goes on the ledger entry and
 * in the audit trail, because a money movement should carry its justification with it.
 */
function RefundPaymentDialog({ order, onClose, onRefunded }: RefundPaymentDialogProps) {
  const [payments, setPayments] = useState<PaymentDto[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setPayments(null)
    setLoadError(null)
    setSubmitError(null)
    setSelectedId(null)
    adminOrderPayments(order.id)
      .then((rows) => {
        if (!alive) return
        setPayments(rows)
        const firstRefundable = rows.find((p) => p.status === PAYMENT_STATUS.Paid && refundable(p) > 0)
        if (firstRefundable) {
          setSelectedId(firstRefundable.id)
          setAmount(String(refundable(firstRefundable)))
        }
      })
      .catch((err) => {
        if (alive) setLoadError(problemFrom(err).detail ?? 'Could not load the payments on this order.')
      })
    return () => {
      alive = false
    }
  }, [order.id])

  const selected = payments?.find((p) => p.id === selectedId) ?? null
  const refundableOfSelected = selected ? refundable(selected) : 0

  async function submit() {
    if (!selectedId) return
    const parsed = Number(amount)
    if (!Number.isFinite(parsed) || parsed <= 0 || parsed > refundableOfSelected + 0.005) {
      setSubmitError(
        `The amount must be more than 0 and no more than ${money(refundableOfSelected, order.currency)}.`,
      )
      return
    }
    setSubmitting(true)
    setSubmitError(null)
    try {
      await adminRefundPayment(selectedId, { amount: parsed, reason: reason.trim() || undefined })
      onRefunded()
    } catch (err) {
      setSubmitError(problemFrom(err).detail ?? 'The refund was refused by the server.')
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Refund a payment"
      description={`Order ${order.orderNumber} — ${money(order.amountPaid, order.currency)} settled`}
      size="md"
      dismissOnBackdrop={!submitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={!selectedId || submitting}>
            {submitting
              ? 'Refunding…'
              : amount && Number(amount) > 0
                ? `Refund ${money(Number(amount), order.currency)}`
                : 'Refund'}
          </Button>
        </>
      }
    >
      {loadError ? <ErrorAlert title="Could not load payments" detail={loadError} /> : null}

      {payments === null ? (
        <Spinner label="Loading payments…" />
      ) : (
        <>
          {payments.length === 0 ? (
            <p className="text-sm text-stone-500">There are no payments recorded on this order.</p>
          ) : (
            <div className="space-y-2">
              {payments.map((payment) => {
                const canRefund = payment.status === PAYMENT_STATUS.Paid && refundable(payment) > 0
                const checked = payment.id === selectedId
                return (
                  <label
                    key={payment.id}
                    className={`flex cursor-pointer items-start gap-3 rounded-xl border px-3 py-2.5 text-sm transition ${
                      canRefund
                        ? checked
                          ? 'border-brand-400 bg-brand-50/60'
                          : 'border-stone-200 bg-white hover:bg-stone-50'
                        : 'border-stone-200 bg-stone-50 opacity-70'
                    }`}
                  >
                    <input
                      type="radio"
                      name="payment"
                      disabled={!canRefund}
                      checked={checked}
                      onChange={() => {
                        setSelectedId(payment.id)
                        setAmount(String(refundable(payment)))
                      }}
                      className="mt-1"
                    />
                    <span className="min-w-0 flex-1">
                      <span className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-mono text-xs font-semibold text-stone-800">
                          {payment.reference}
                        </span>
                        <span className="text-xs text-stone-500">
                          {PAYMENT_STATUSES[payment.status]}
                        </span>
                      </span>
                      <span className="mt-0.5 block text-xs text-stone-500">
                        {money(payment.amount, payment.currency)}
                        {canRefund
                          ? ` · ${money(refundable(payment), payment.currency)} refundable`
                          : payment.refundedAmount
                            ? ` · ${money(payment.refundedAmount, payment.currency)} already refunded`
                            : ''}
                      </span>
                    </span>
                  </label>
                )
              })}
            </div>
          )}

          {selected ? (
            <div className="mt-4 space-y-3">
              <label className="block">
                <span className="mb-1.5 block text-sm font-medium text-stone-700">Refund amount</span>
                <input
                  type="number"
                  min={0.01}
                  max={refundableOfSelected}
                  step={0.01}
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  className={controlClass}
                />
                <span className="mt-1 block text-xs text-stone-500">
                  {money(refundableOfSelected, order.currency)} available to refund on{' '}
                  {selected.reference}. Leave at the full amount to refund it all.
                </span>
              </label>
              <TextArea
                label="Reason (optional)"
                id="refund-reason"
                rows={2}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="Why is the money going back?"
              />
            </div>
          ) : null}

          {submitError ? (
            <div className="mt-4">
              <ErrorAlert title="Refund refused" detail={submitError} />
            </div>
          ) : null}

          <p className="mt-4 text-xs text-stone-500">
            The refund is booked as a compensating ledger entry, the order's settled figure drops by
            the same amount, and a fully-paid order's invoice reopens. Both parties are notified and
            the action is written to the audit trail as yours.
          </p>
        </>
      )}
    </Modal>
  )
}