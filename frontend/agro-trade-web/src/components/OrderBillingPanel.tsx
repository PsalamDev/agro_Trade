import { useEffect, useState } from 'react'
import { Button, ErrorAlert, Field } from './ui'
import { InvoiceStatusPill } from './badges'
import { generateInvoice, getOrderInvoice, getOrderReceipts } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtcDate } from '../lib/format'
import { ORDER_STATUS } from '../types/trade'
import type { InvoiceDto, OrderDetailDto, ReceiptDto } from '../types/trade'

function formatDate(value: string | null) {
  return value ? formatUtcDate(value) : null
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
  onChanged: () => Promise<void> | void
}

/**
 * The document pair around a payment: the invoice the buyer is asked to pay, and the receipts
 * that evidence the money arriving.
 *
 * The invoice is one per order and is issued by the seller. The receipts are written *by the
 * settlement itself* — gateway webhook or seller-recorded transfer — so this screen never
 * triggers one; it reads what settlement has already produced.
 */
export function OrderBillingPanel({ order, onChanged }: Props) {
  const [invoice, setInvoice] = useState<InvoiceDto | null>(null)
  const [invoiceMissing, setInvoiceMissing] = useState(false)
  const [receipts, setReceipts] = useState<ReceiptDto[]>([])
  const [issuing, setIssuing] = useState(false)
  const [dueDate, setDueDate] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const isClosed = order.status === ORDER_STATUS.Cancelled || order.status === ORDER_STATUS.Completed
  const canIssue = order.isSeller && invoiceMissing && !isClosed

  useEffect(() => {
    let cancelled = false
    void getOrderInvoice(order.id)
      .then((value) => {
        if (!cancelled) {
          setInvoice(value)
          setInvoiceMissing(false)
        }
      })
      .catch((err) => {
        if (cancelled) return
        const problem = problemFrom(err)
        // "No invoice issued yet" is a state, not an error.
        if (problem.code === 'INVOICE_NOT_FOUND') {
          setInvoice(null)
          setInvoiceMissing(true)
        } else {
          setError(problem)
        }
      })
    return () => {
      cancelled = true
    }
  }, [order.id])

  useEffect(() => {
    let cancelled = false
    void getOrderReceipts(order.id)
      .then((rows) => {
        if (!cancelled) setReceipts(rows)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
    return () => {
      cancelled = true
    }
  }, [order.id, paymentsVersion(order)])

  async function handleIssue() {
    setIssuing(true)
    setError(null)
    try {
      await generateInvoice(order.id, {
        dueDateUtc: dueDate ? `${dueDate}T00:00:00Z` : undefined,
      })
      await onChanged()
      // Reload so the freshly issued invoice replaces the empty state.
      const issued = await getOrderInvoice(order.id)
      setInvoice(issued)
      setInvoiceMissing(false)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setIssuing(false)
    }
  }

  return (
    <section className="mt-8">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Invoice</h2>
        {invoice ? <InvoiceStatusPill status={invoice.status} /> : null}
      </div>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'INVOICE_EXISTS'
                ? 'An invoice has already been issued for this order.'
                : error.detail
            }
          />
        </div>
      ) : null}

      {invoice ? (
        <div className="mt-3 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <p className="font-mono text-sm font-semibold text-stone-800">{invoice.invoiceNumber}</p>
            <p className="font-medium">{money(invoice.amount, invoice.currency)}</p>
          </div>
          <p className="mt-1 text-xs text-stone-500">
            Issued {formatDate(invoice.issuedAtUtc)}
            {invoice.dueDateUtc ? ` · due ${formatDate(invoice.dueDateUtc)}` : ''}
            {invoice.paidAtUtc ? ` · paid ${formatDate(invoice.paidAtUtc)}` : ''}
          </p>
        </div>
      ) : invoiceMissing ? (
        canIssue ? (
          <div className="mt-3 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
            <h3 className="text-sm font-semibold text-stone-700">Issue the invoice</h3>
            <p className="mt-1 text-xs text-stone-500">
              Raises the order's invoice for {money(order.total, order.currency)}, in full. One per
              order — the buyer pays against this document.
            </p>
            <div className="mt-3 flex flex-wrap items-end gap-3">
              <Field
                label="Due date (optional)"
                id="invoice-due-date"
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
                hint=""
              />
              <Button disabled={issuing} onClick={() => void handleIssue()}>
                {issuing ? 'Issuing…' : `Issue invoice for ${money(order.total, order.currency)}`}
              </Button>
            </div>
          </div>
        ) : isClosed ? (
          <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
            This order is {order.status === ORDER_STATUS.Cancelled ? 'cancelled' : 'complete'}, so no
            invoice will be issued.
          </p>
        ) : (
          <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
            No invoice has been issued for this order yet.
          </p>
        )
      ) : null}

      <div className="mt-8">
        <h3 className="text-sm font-semibold text-stone-700">Receipts</h3>
        <p className="mt-1 text-xs text-stone-500">
          Written automatically when a payment settles — one per settled payment, whether it
          arrived through a gateway or by bank transfer.
        </p>

        {receipts.length === 0 ? (
          <p className="mt-2 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-5 text-center text-sm text-stone-500">
            No receipts yet. They appear here the moment a payment settles.
          </p>
        ) : (
          <ul className="mt-3 divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
            {receipts.map((receipt) => (
              <li key={receipt.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
                <div className="min-w-0">
                  <p className="font-mono text-sm font-semibold text-stone-800">{receipt.receiptNumber}</p>
                  {receipt.notes ? <p className="mt-0.5 text-xs text-stone-500">{receipt.notes}</p> : null}
                </div>
                <div className="text-right">
                  <p className="font-medium">{money(receipt.amount, receipt.currency)}</p>
                  <p className="text-xs text-stone-400">Issued {formatDate(receipt.issuedAtUtc)}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}

/** The receipts list reloads when the payment set changes — a settlement adds a receipt. */
function paymentsVersion(order: OrderDetailDto) {
  return order.payments.map((p) => `${p.id}:${p.status}`).join('|')
}