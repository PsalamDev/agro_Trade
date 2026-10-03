import { useEffect, useState } from 'react'
import { Button, ErrorAlert, Field, SelectField } from './ui'
import { OrderPaymentPill } from './badges'
import { getOrderTransactions, initiatePayment, recordManualPayment, settleSandboxPayment } from '../lib/trade'
import { problemFrom } from '../lib/api'
import {
  ORDER_STATUS,
  PAYMENT_METHODS,
  PAYMENT_METHOD,
  PAYMENT_PROVIDERS,
  PAYMENT_STATUS,
  TRANSACTION_TYPES,
} from '../types/trade'
import type { OrderDetailDto, PaymentDto, TransactionDto } from '../types/trade'

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
 * Payments and the ledger for one order (tasks 2.4 and 2.5).
 *
 * Two rails, one financial record. The buyer can raise a gateway charge; the seller can record a
 * bank transfer that has already landed. Both write to the ledger below, which is what the buyer
 * reconciles against their bank statement.
 *
 * Settlement is deliberately not something this screen can do on its own. A gateway charge starts
 * pending and only becomes paid when the gateway says so, which in the sandbox means a webhook.
 * The "Simulate" control stands in for the payer's checkout page and drives the real signed event
 * through the real handler, rather than flipping a status directly — otherwise the screen would
 * prove nothing about the path that actually matters.
 *
 * The asymmetry is deliberate and worth stating to anyone who asks why the paying side is read
 * only: the buyer sends the money, but only the beneficiary of the account can see it arrive. A
 * buyer recording their own transfer would let anyone mark an order paid against funds the
 * platform cannot verify.
 */
export function OrderPaymentsPanel({ order, payments, onChanged }: Props) {
  const [ledger, setLedger] = useState<TransactionDto[]>([])
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [recording, setRecording] = useState(false)

  const settled = payments.filter((p) => p.status === PAYMENT_STATUS.Paid).reduce((sum, p) => sum + p.amount, 0)
  const outstanding = Math.max(0, order.total - settled)
  const inFlight = payments.some((p) => p.status === PAYMENT_STATUS.Pending)

  const isClosed = order.status === ORDER_STATUS.Cancelled || order.status === ORDER_STATUS.Completed
  const canPay = order.isBuyer && !isClosed && outstanding > 0 && !inFlight
  const canRecord = order.isSeller && !isClosed && outstanding > 0

  // The ledger is a separate read from the payments list: it is a chronological financial record,
  // not a list of attempts, and it grows independently of the order's payments.
  useEffect(() => {
    let cancelled = false
    void getOrderTransactions(order.id)
      .then((rows) => {
        if (!cancelled) setLedger(rows)
      })
      .catch(() => {
        // The ledger is supplementary. A failure to load it should not take the payments list down
        // with it, but it should be visible rather than silently empty.
        if (!cancelled) setError({ code: 'LEDGER_UNAVAILABLE', detail: 'The payment ledger could not be loaded.' })
      })
    return () => {
      cancelled = true
    }
  }, [order.id, payments.length])

  async function handlePay() {
    setBusyId('new')
    setError(null)
    try {
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

  async function refreshLedger() {
    setLedger(await getOrderTransactions(order.id))
    await onChanged()
  }

  return (
    <section className="mt-8">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Payments</h2>
        <p className="text-xs text-stone-500">
          {settled > 0
            ? `${money(settled, order.currency)} of ${money(order.total, order.currency)} received`
            : 'Nothing received yet'}
        </p>
      </div>
      <p className="mt-1 text-sm text-stone-500">
        {money(order.total, order.currency)} total for this order.
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
                    : error.code === 'REMITTANCE_REFERENCE_REUSED'
                      ? 'That remittance reference is already recorded on this order. A bank transfer has one trace id — recording it twice would double-count it.'
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
                  {payment.remittanceReference ? (
                    <p className="mt-0.5 text-xs text-stone-500">
                      <span className="font-mono">{payment.remittanceReference}</span>
                      {payment.bankName ? ` · ${payment.bankName}` : ''}
                    </p>
                  ) : null}
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

      {canRecord ? (
        recording ? (
          <RecordPaymentForm
            order={order}
            outstanding={outstanding}
            onCancel={() => setRecording(false)}
            onRecorded={async () => {
              setRecording(false)
              await refreshLedger()
            }}
          />
        ) : (
          <div className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
            <h3 className="text-sm font-semibold text-stone-700">Received a bank transfer?</h3>
            <p className="mt-1 text-xs text-stone-500">
              Record money that has already landed against this order. This is the record for
              corridors with no payment gateway — a T/T remittance or an honoured letter of credit.
            </p>
            <div className="mt-3">
              <Button size="sm" onClick={() => setRecording(true)}>
                Record a payment
              </Button>
            </div>
          </div>
        )
      ) : null}

      <Ledger entries={ledger} currency={order.currency} />
    </section>
  )
}

/**
 * The seller-side manual recording form.
 *
 * The remittance reference is the field that makes the record meaningful: it is the bank's own
 * trace id, and it is what makes a duplicate detectable and the entry auditable. It is required,
 * and the server refuses the same one twice on an order.
 */
function RecordPaymentForm({
  order,
  outstanding,
  onCancel,
  onRecorded,
}: {
  order: OrderDetailDto
  outstanding: number
  onCancel: () => void
  onRecorded: () => Promise<void>
}) {
  const [amount, setAmount] = useState(outstanding.toFixed(2))
  const [method, setMethod] = useState(String(PAYMENT_METHOD.BankTransfer))
  const [remittanceReference, setRemittanceReference] = useState('')
  const [bankName, setBankName] = useState('')
  const [valueDate, setValueDate] = useState('')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const parsedAmount = Number(amount)
  const amountInvalid = !Number.isFinite(parsedAmount) || parsedAmount <= 0
  const overBalance = Number.isFinite(parsedAmount) && parsedAmount > outstanding
  const referenceMissing = remittanceReference.trim().length === 0
  // A date input yields YYYY-MM-DD with no zone. Sending that as-is is read as UTC, which is the
  // right reading for a value date and avoids the local-midnight shift that `new Date(string)`
  // would introduce.
  const valueDateInvalid = valueDate.length > 0 && valueDate > new Date().toISOString().slice(0, 10)

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (amountInvalid || overBalance || referenceMissing || valueDateInvalid) return

    setBusy(true)
    setError(null)
    try {
      await recordManualPayment(order.id, {
        amount: parsedAmount,
        method: Number(method),
        remittanceReference: remittanceReference.trim(),
        bankName: bankName.trim() || undefined,
        valueDateUtc: valueDate ? `${valueDate}T00:00:00Z` : undefined,
        notes: notes.trim() || undefined,
        idempotencyKey: crypto.randomUUID(),
      })
      await onRecorded()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
      <h3 className="text-sm font-semibold text-stone-700">Record an off-platform payment</h3>
      <p className="mt-1 text-xs text-stone-500">
        {money(outstanding, order.currency)} outstanding. This records the money as already
        received, so only do it once the funds are on the account.
      </p>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'REMITTANCE_REFERENCE_REUSED'
                ? 'That remittance reference is already recorded on this order. A bank transfer has one trace id — recording it twice would double-count it.'
                : error.code === 'VALUE_DATE_IN_FUTURE'
                  ? 'The value date cannot be in the future — the funds have to have arrived.'
                  : error.detail
            }
          />
        </div>
      ) : null}

      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <Field
          label={`Amount (${order.currency})`}
          id="manual-amount"
          type="number"
          step="0.01"
          min="0"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          required
          hint={
            amountInvalid
              ? 'Enter an amount greater than zero.'
              : overBalance
                ? `Only ${money(outstanding, order.currency)} is outstanding.`
                : undefined
          }
        />
        <SelectField
          label="Method"
          id="manual-method"
          value={method}
          onChange={(e) => setMethod(e.target.value)}
          options={['Bank transfer (T/T)', 'Letter of credit']}
          values={[PAYMENT_METHOD.BankTransfer, PAYMENT_METHOD.LetterOfCredit]}
        />
        <Field
          label="Remittance reference"
          id="manual-reference"
          value={remittanceReference}
          onChange={(e) => setRemittanceReference(e.target.value)}
          required
          placeholder="UTR-2026-001234"
          hint={
            referenceMissing
              ? "The bank's own trace id. Required, and what makes a duplicate detectable."
              : 'The bank’s own trace id. Unique per order.'
          }
        />
        <Field
          label="Bank"
          id="manual-bank"
          value={bankName}
          onChange={(e) => setBankName(e.target.value)}
          placeholder="First Bank"
          hint="Optional. Helps the buyer match it against their statement."
        />
        <Field
          label="Value date"
          id="manual-value-date"
          type="date"
          value={valueDate}
          onChange={(e) => setValueDate(e.target.value)}
          hint={
            valueDateInvalid
              ? 'The funds have to have arrived, so this cannot be in the future.'
              : 'When the funds landed. Defaults to today.'
          }
        />
        <Field
          label="Notes"
          id="manual-notes"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          placeholder="Optional"
          hint=""
        />
      </div>

      <div className="mt-4 flex items-center gap-2">
        <Button type="submit" size="sm" disabled={busy || amountInvalid || overBalance || referenceMissing || valueDateInvalid}>
          {busy ? 'Recording…' : 'Record payment'}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

/**
 * The order's ledger, oldest first.
 *
 * Distinct from the payments list above on purpose: a payment is an attempt that can change state,
 * while a ledger entry is a fact about money that moved. Bank transfers and gateway settlements
 * appear in the same list, which is what makes it reconcilable against a bank statement.
 */
function Ledger({ entries, currency }: { entries: TransactionDto[]; currency: string }) {
  return (
    <div className="mt-8">
      <h3 className="text-sm font-semibold text-stone-700">Payment ledger</h3>
      <p className="mt-1 text-xs text-stone-500">
        One entry per movement of money, oldest first. Bank transfers and card settlements both
        appear here.
      </p>

      {entries.length === 0 ? (
        <p className="mt-2 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-5 text-center text-sm text-stone-500">
          Nothing has moved through the ledger yet.
        </p>
      ) : (
        <ul className="mt-3 divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
          {entries.map((entry) => (
            <li key={entry.id} className="px-4 py-3">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <p className="font-medium">{money(entry.amount, entry.currency)}</p>
                <p className="text-xs text-stone-400">{formatDate(entry.occurredAtUtc)}</p>
              </div>
              {entry.narrative ? <p className="mt-0.5 text-sm text-stone-600">{entry.narrative}</p> : null}
              <p className="mt-0.5 text-xs text-stone-400">
                {entry.reference}
                {entry.paymentReference ? ` · from ${entry.paymentReference}` : ''}
                {entry.externalReference ? ` · ${entry.externalReference}` : ''}
                {` · ${TRANSACTION_TYPES[entry.type] ?? 'Entry'}`}
              </p>
            </li>
          ))}
        </ul>
      )}

      <p className="mt-2 text-xs text-stone-400">
        All amounts in {currency}. The ledger is append-only: entries are never edited or removed.
      </p>
    </div>
  )
}