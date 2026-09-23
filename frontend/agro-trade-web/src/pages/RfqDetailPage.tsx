import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { IncotermBadge, QuotationPill, RfqStatusPill, VerifiedBadge } from '../components/badges'
import { QuotationForm } from '../components/QuotationForm'
import { ThreadView } from '../components/ThreadView'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { acceptQuotation, getConversations, getRfq } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { MESSAGE_CONTEXT_TYPE, PAYMENT_TERMS, PRICE_UNITS, SHIPMENT_MODES, labelOf } from '../types/trade'
import type { ConversationDto, QuotationDto, TradeRequestDetailDto } from '../types/trade'

function FieldRow({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">{label}</dt>
      <dd className="mt-0.5 text-sm font-medium">{value}</dd>
    </div>
  )
}

export default function RfqDetailPage() {
  const { id } = useParams()
  const [rfq, setRfq] = useState<TradeRequestDetailDto | null>(null)
  const [conversations, setConversations] = useState<ConversationDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [acceptingId, setAcceptingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<{ code?: string; detail?: string } | null>(null)
  const [openThread, setOpenThread] = useState<{ supplierUserId: string; threadKey: string | null } | null>(null)
  const [supplierThreadKey, setSupplierThreadKey] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      const [detail, convs] = await Promise.all([getRfq(id), getConversations()])
      setRfq(detail)
      setConversations(convs.items)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    void load()
  }, [load])

  const rfqThreads = useMemo(
    () => conversations.filter((c) => c.contextType === MESSAGE_CONTEXT_TYPE.Rfq && c.contextId === id),
    [conversations, id],
  )

  const supplierThreads = useMemo(() => {
    const map = new Map<string, ConversationDto>()
    for (const conversation of rfqThreads) map.set(conversation.otherUserId, conversation)
    return map
  }, [rfqThreads])

  async function handleAccept(quotation: QuotationDto) {
    setAcceptingId(quotation.id)
    setActionError(null)
    try {
      await acceptQuotation(quotation.id)
      await load()
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setAcceptingId(null)
    }
  }

  function openSupplierThread(supplierUserId: string, threadKey: string | null) {
    setOpenThread({ supplierUserId, threadKey })
  }

  const canAccept = (quotation: QuotationDto) =>
    rfq !== null &&
    [0, 1, 2].includes(quotation.status) &&
    [0, 1, 2].includes(rfq.status)

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <Link to="/rfqs" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to RFQ directory
        </Link>

        {error ? (
          <div className="mt-6">
            <ErrorAlert
              title="RFQ not available"
              detail={
                error.code === 'RFQ_NOT_PARTICIPANT'
                  ? 'Only the buyer and quoting suppliers can view this RFQ. Quote on it from your RFQ list to participate.'
                  : error.detail
              }
            />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : rfq ? (
          <div className="mt-6 grid gap-6 lg:grid-cols-5">
            <div className="space-y-6 lg:col-span-3">
              <Card>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-mono text-xs text-stone-400">{rfq.rfqNumber}</p>
                    <h1 className="mt-1 text-2xl font-bold tracking-tight">{rfq.title}</h1>
                    <p className="mt-1 text-sm text-stone-500">
                      {rfq.isBuyer ? 'You (buyer)' : rfq.buyerDisplayName} · {rfq.isMine ? 'Asked by you' : 'Published by buyer'}
                    </p>
                  </div>
                  <RfqStatusPill status={rfq.status} />
                </div>
                {rfq.description ? <p className="mt-4 text-sm leading-relaxed text-stone-600">{rfq.description}</p> : null}

                <dl className="mt-6 grid grid-cols-2 gap-4 rounded-lg bg-stone-50 p-4 sm:grid-cols-3">
                  <FieldRow label="Destination" value={`${rfq.destinationCountry}${rfq.destinationPortOrCity ? ` · ${rfq.destinationPortOrCity}` : ''}`} />
                  <FieldRow label="Quantity" value={`${Number(rfq.quantity).toLocaleString()} ${labelOf(PRICE_UNITS, rfq.unit).replace('Per ', '').toLowerCase()}`} />
                  <FieldRow label="Preferred incoterm" value={rfq.preferredIncoterm !== null && rfq.preferredIncoterm !== undefined ? (labelOf(['EXW', 'FCA', 'FAS', 'FOB', 'CFR', 'CIF', 'CPT', 'CIP', 'DAP', 'DPU', 'DDP'], rfq.preferredIncoterm) ?? '-') : '—'} />
                  <FieldRow label="Transport" value={rfq.transportMode === null || rfq.transportMode === undefined ? '—' : labelOf(SHIPMENT_MODES, rfq.transportMode)} />
                  <FieldRow label="Currency" value={rfq.preferredCurrency} />
                  <FieldRow label="Insurance" value={rfq.insuranceRequired ? 'Required' : 'Not required'} />
                  {rfq.paymentTermsPreferred !== null && rfq.paymentTermsPreferred !== undefined ? (
                    <FieldRow label="Payment terms preferred" value={labelOf(PAYMENT_TERMS, rfq.paymentTermsPreferred)} />
                  ) : null}
                  {rfq.deliveryDeadline ? <FieldRow label="Delivery deadline" value={new Date(rfq.deliveryDeadline).toLocaleDateString()} /> : null}
                  <FieldRow label="RFQ expires" value={new Date(rfq.expiresAt).toLocaleDateString()} />
                  {rfq.budgetMin !== null || rfq.budgetMax !== null ? (
                    <FieldRow
                      label="Budget"
                      value={
                        `${rfq.preferredCurrency} ${rfq.budgetMin !== null ? Number(rfq.budgetMin).toLocaleString() : '—'}` +
                        ` – ${rfq.budgetMax !== null ? Number(rfq.budgetMax).toLocaleString() : '—'}`
                      }
                    />
                  ) : null}
                </dl>

                {rfq.targetSpecs && Object.keys(rfq.targetSpecs).length > 0 ? (
                  <div className="mt-6">
                    <h2 className="text-lg font-semibold">Target specifications</h2>
                    <dl className="mt-3 grid gap-3 sm:grid-cols-2">
                      {Object.entries(rfq.targetSpecs).map(([key, value]) => (
                        <FieldRow key={key} label={key} value={value} />
                      ))}
                    </dl>
                  </div>
                ) : null}

                {rfq.certificationsRequired.length > 0 ? (
                  <div className="mt-6">
                    <h2 className="text-lg font-semibold">Certifications required</h2>
                    <ul className="mt-3 flex flex-wrap gap-2">
                      {rfq.certificationsRequired.map((cert) => (
                        <li key={cert} className="rounded-full bg-brand-50 px-3 py-1 text-sm font-medium text-brand-800">
                          {cert}
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </Card>

              {rfq.isBuyer ? (
                <Card>
                  <div className="flex items-center justify-between">
                    <h2 className="text-lg font-semibold">Quotations ({rfq.quotations.length})</h2>
                    {[0, 1, 2].includes(rfq.status) ? (
                      <span className="text-sm text-stone-500">Open for quotes</span>
                    ) : (
                      <RfqStatusPill status={rfq.status} />
                    )}
                  </div>
                  {actionError ? (
                    <div className="mt-3">
                      <ErrorAlert detail={actionError.detail} />
                    </div>
                  ) : null}
                  {rfq.quotations.length === 0 ? (
                    <div className="mt-4">
                      <EmptyState title="No quotations yet" body="Suppliers will appear here as soon as they quote." />
                    </div>
                  ) : (
                    <ul className="mt-4 space-y-4">
                      {rfq.quotations.map((quotation) => {
                        const thread = supplierThreads.get(quotation.supplierOwnerUserId)
                        return (
                          <li key={quotation.id} className="rounded-xl border border-stone-200 p-4">
                            <div className="flex flex-wrap items-start justify-between gap-3">
                              <div className="flex items-center gap-2">
                                <p className="font-semibold">{quotation.supplierTradingName}</p>
                                <VerifiedBadge visible={quotation.isSupplierVerifiedBadgeVisible} />
                                <QuotationPill status={quotation.status} />
                              </div>
                              <p className="text-lg font-bold text-brand-700">
                                {quotation.currency} {Number(quotation.total).toLocaleString()}
                              </p>
                            </div>
                            <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-4">
                              <div>
                                <dt className="text-xs text-stone-500">Incoterm</dt>
                                <dd className="font-medium">
                                  <IncotermBadge value={quotation.incoterms} />
                                </dd>
                              </div>
                              <div>
                                <dt className="text-xs text-stone-500">Lead time</dt>
                                <dd className="font-medium">{quotation.leadTimeDays} days</dd>
                              </div>
                              <div>
                                <dt className="text-xs text-stone-500">Payment</dt>
                                <dd className="font-medium">{labelOf(PAYMENT_TERMS, quotation.paymentTerms)}</dd>
                              </div>
                              <div>
                                <dt className="text-xs text-stone-500">Ports</dt>
                                <dd className="font-medium">
                                  {quotation.portOfLoading ?? '—'} → {quotation.portOfDischarge ?? '—'}
                                </dd>
                              </div>
                            </dl>
                            {quotation.notes ? <p className="mt-3 text-sm text-stone-600">{quotation.notes}</p> : null}
                            <div className="mt-4 flex flex-wrap items-center gap-3">
                              {canAccept(quotation) ? (
                                <button
                                  type="button"
                                  disabled={acceptingId === quotation.id}
                                  onClick={() => void handleAccept(quotation)}
                                  className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:opacity-60"
                                >
                                  {acceptingId === quotation.id ? 'Accepting…' : 'Accept quotation'}
                                </button>
                              ) : null}
                              <button
                                type="button"
                                onClick={() =>
                                  openSupplierThread(quotation.supplierOwnerUserId, thread?.threadKey ?? null)
                                }
                                className="rounded-lg border border-stone-300 bg-white px-4 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50"
                              >
                                {thread ? 'Open negotiation thread' : 'Message supplier'}
                              </button>
                            </div>
                            {openThread?.supplierUserId === quotation.supplierOwnerUserId ? (
                              <div className="mt-4 border-t border-stone-100 pt-4">
                                <ThreadView
                                  threadKey={openThread.threadKey}
                                  onThreadKeyChange={(key) => setOpenThread((t) => (t ? { ...t, threadKey: key } : t))}
                                  contextType={MESSAGE_CONTEXT_TYPE.Rfq}
                                  contextId={rfq.id}
                                  recipientUserId={quotation.supplierOwnerUserId}
                                />
                              </div>
                            ) : null}
                          </li>
                        )
                      })}
                    </ul>
                  )}
                </Card>
              ) : (
                <Card>
                  <h2 className="text-lg font-semibold">Your quotation</h2>
                  {rfq.quotations.some((q) => q.isMine) ? (
                    (() => {
                      const mine = rfq.quotations.find((q) => q.isMine)!
                      return (
                        <div>
                          <div className="mt-3 flex flex-wrap items-center gap-2">
                            <QuotationPill status={mine.status} />
                            <p className="text-lg font-bold text-brand-700">
                              {mine.currency} {Number(mine.total).toLocaleString()}
                            </p>
                            <IncotermBadge value={mine.incoterms} />
                          </div>
                          {mine.notes ? <p className="mt-2 text-sm text-stone-600">{mine.notes}</p> : null}
                          <div className="mt-4">
                            <h3 className="text-sm font-semibold text-stone-700">Negotiate with the buyer</h3>
                            <div className="mt-2">
                              <ThreadView
                                threadKey={supplierThreadKey ?? rfqThreads[0]?.threadKey ?? null}
                                onThreadKeyChange={setSupplierThreadKey}
                                contextType={MESSAGE_CONTEXT_TYPE.Rfq}
                                contextId={rfq.id}
                              />
                            </div>
                          </div>
                        </div>
                      )
                    })()
                  ) : (
                    <div className="mt-4">
                      {[0, 1, 2].includes(rfq.status) ? (
                        <QuotationForm rfqId={rfq.id} currency={rfq.preferredCurrency} onQuoted={() => void load()} />
                      ) : (
                        <p className="text-sm text-stone-500">This RFQ is closed for quotations.</p>
                      )}
                    </div>
                  )}
                </Card>
              )}
            </div>

            <div className="space-y-6 lg:col-span-2">
              <Card className="bg-brand-50/60">
                <h2 className="text-lg font-semibold">Next steps</h2>
                <ul className="mt-3 list-inside list-disc space-y-2 text-sm text-stone-600">
                  {rfq.isBuyer ? (
                    <>
                      <li>Compare quotations side by side (trade terms drive the contract).</li>
                      <li>Accept one offer — others are declined automatically.</li>
                      <li>Negotiate directly with a supplier in the thread below their quote.</li>
                    </>
                  ) : (
                    <>
                      <li>Submit your best terms with a firm total price.</li>
                      <li>Acceptance converts your quotation into the buyer’s order (Phase 2).</li>
                    </>
                  )}
                </ul>
              </Card>
              <Card>
                <h2 className="text-lg font-semibold">About this marketplace</h2>
                <p className="mt-2 text-sm leading-relaxed text-stone-600">
                  All RFQ activity is visible to the buyer, and quotations carry enforceable trade terms —
                  incoterms, ports, lead time, and payment terms.
                </p>
              </Card>
            </div>
          </div>
        ) : null}
      </main>
    </div>
  )
}