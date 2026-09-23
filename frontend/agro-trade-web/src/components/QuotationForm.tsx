import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { createQuotation, getMyBusinesses } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { Card, ErrorAlert, Field, SelectField, SubmitButton, TextArea } from './ui'
import { INCOTERMS, PAYMENT_TERMS, PAYMENT_TERM } from '../types/trade'
import type { BusinessSummaryDto } from '../types/trade'

export function QuotationForm({ rfqId, currency, onQuoted }: { rfqId: string; currency: string; onQuoted: () => void }) {
  const [businesses, setBusinesses] = useState<BusinessSummaryDto[]>([])
  const [businessId, setBusinessId] = useState('')
  const [total, setTotal] = useState('')
  const [incoterm, setIncoterm] = useState('5')
  const [portOfLoading, setPortOfLoading] = useState('')
  const [portOfDischarge, setPortOfDischarge] = useState('')
  const [leadTimeDays, setLeadTimeDays] = useState('')
  const [paymentTerms, setPaymentTerms] = useState(String(PAYMENT_TERM.TtPartialDeposit))
  const [notes, setNotes] = useState('')
  const [validUntil, setValidUntil] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)

  useEffect(() => {
    let cancelled = false
    void getMyBusinesses()
      .then((list) => {
        if (!cancelled) setBusinesses(list)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await createQuotation(rfqId, {
        currency,
        incoterms: Number(incoterm),
        portOfLoading: portOfLoading || undefined,
        portOfDischarge: portOfDischarge || undefined,
        leadTimeDays: Number(leadTimeDays),
        paymentTerms: Number(paymentTerms),
        total: Number(total),
        notes: notes || undefined,
        validUntil: validUntil ? new Date(`${validUntil}T00:00:00Z`).toISOString() : undefined,
        businessId: businessId || undefined,
      })
      onQuoted()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <Card>
      <h2 className="text-lg font-semibold">Submit a quotation</h2>
      <p className="mt-1 text-sm text-stone-500">One quotation per business per RFQ. Terms carry into the accepted order.</p>
      <form onSubmit={handleSubmit} className="mt-4 space-y-4">
        <ErrorAlert title={error?.detail} fieldErrors={error?.fieldErrors} />
        {businesses.length > 1 ? (
          <SelectField
            label="Quoting business"
            id="quote-business"
            options={businesses.map((b) => b.tradingName)}
            values={businesses.map((b) => b.id)}
            value={businessId}
            onChange={(e) => setBusinessId(e.target.value)}
          />
        ) : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={`Total price (${currency}) *`} id="quote-total" type="number" min="0" step="any" required value={total} onChange={(e) => setTotal(e.target.value)} />
          <Field label="Lead time (days) *" id="quote-lead" type="number" min="0" required value={leadTimeDays} onChange={(e) => setLeadTimeDays(e.target.value)} />
          <SelectField label="Incoterm *" id="quote-incoterm" options={INCOTERMS} value={incoterm} onChange={(e) => setIncoterm(e.target.value)} />
          <SelectField
            label="Payment terms *"
            id="quote-payment"
            options={PAYMENT_TERMS}
            values={Object.values(PAYMENT_TERM)}
            value={paymentTerms}
            onChange={(e) => setPaymentTerms(e.target.value)}
          />
          <Field label="Port of loading" id="quote-pol" value={portOfLoading} onChange={(e) => setPortOfLoading(e.target.value)} placeholder="Mombasa" />
          <Field label="Port of discharge" id="quote-pod" value={portOfDischarge} onChange={(e) => setPortOfDischarge(e.target.value)} placeholder="Lagos / Apapa" />
        </div>
        <Field label="Offer valid until" id="quote-valid" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
        <TextArea label="Notes" id="quote-notes" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} placeholder="Included costs, packing, inspection, samples…" />
        <SubmitButton type="submit" pending={pending}>
          Submit quotation
        </SubmitButton>
      </form>
    </Card>
  )
}