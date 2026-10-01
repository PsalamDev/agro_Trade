import { useState } from 'react'
import { DocumentStatusPill } from './badges'
import { Button, ErrorAlert, SelectField } from './ui'
import { deleteOrderDocument, uploadOrderDocument } from '../lib/trade'
import { problemFrom } from '../lib/api'
import {
  DOCUMENT_STATUS,
  ORDER_STATUS,
  TRADE_DOCUMENT_TYPE_LABELS,
  TRADE_DOCUMENT_TYPE_VALUES,
  tradeDocumentLabel,
} from '../types/trade'
import type { OrderDetailDto, TradeDocumentDto } from '../types/trade'

function fileSize(bytes: number | null) {
  if (!bytes) return null
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function formatDate(value: string | null) {
  if (!value) return null
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

interface Props {
  order: OrderDetailDto
  documents: TradeDocumentDto[]
  /** The signed-in user's id, used to offer Withdraw only on what they uploaded. */
  userId: string | null
  onChanged: () => Promise<void> | void
}

/**
 * Trade paperwork for one order, shared by both parties.
 *
 * Both sides upload, because the paperwork genuinely goes both ways — the seller raises the
 * invoice and the bill of lading, the buyer countersigns and supplies the import permit. There
 * is no per-document visibility: everything on the order is visible to both parties, which is
 * why this is one list rather than two.
 */
export function OrderDocumentsPanel({ order, documents, userId, onChanged }: Props) {
  const [docType, setDocType] = useState(String(TRADE_DOCUMENT_TYPE_VALUES[0]))
  const [reference, setReference] = useState('')
  const [issuedOn, setIssuedOn] = useState('')
  const [expiresOn, setExpiresOn] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [uploading, setUploading] = useState(false)
  const [withdrawingId, setWithdrawingId] = useState<string | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  // Both parties may upload, so any of them sees the form. A closed order is the one thing
  // that stops it: the trade has unwound or finished, and paperwork against it would be a
  // record of something that will not happen.
  const isClosed = order.status === ORDER_STATUS.Cancelled || order.status === ORDER_STATUS.Completed
  const canUpload = (order.isBuyer || order.isSeller) && !isClosed

  // The server refuses an expiry before the issue date; saying so here is kinder than a 400.
  const expiryBeforeIssue =
    issuedOn !== '' && expiresOn !== '' && new Date(expiresOn) < new Date(issuedOn)

  function resetForm() {
    setReference('')
    setIssuedOn('')
    setExpiresOn('')
    setFile(null)
  }

  async function handleUpload(event: React.FormEvent) {
    event.preventDefault()
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      await uploadOrderDocument(
        order.id,
        {
          docType: Number(docType),
          documentReference: reference.trim() || undefined,
          issuedOnUtc: issuedOn || undefined,
          expiresOnUtc: expiresOn || undefined,
        },
        file,
      )
      resetForm()
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setUploading(false)
    }
  }

  async function handleWithdraw(documentId: string) {
    setWithdrawingId(documentId)
    setError(null)
    try {
      await deleteOrderDocument(documentId)
      await onChanged()
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setWithdrawingId(null)
    }
  }

  const unverified = documents.filter((d) => d.status === DOCUMENT_STATUS.Uploaded).length
  const verified = documents.filter((d) => d.status === DOCUMENT_STATUS.Verified).length

  return (
    <section className="mt-8">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Documents</h2>
        <p className="text-xs text-stone-500">
          {documents.length === 0
            ? 'None attached yet'
            : `${documents.length} attached · ${verified} verified · ${unverified} awaiting review`}
        </p>
      </div>
      <p className="mt-1 text-sm text-stone-500">
        Invoices, packing lists, certificates and permits for this shipment. Both parties can
        attach paperwork, and both can see everything here.
      </p>

      {error ? (
        <div className="mt-3">
          <ErrorAlert
            detail={
              error.code === 'ORDER_CLOSED'
                ? 'This order is closed, so no further documents can be added.'
                : error.code === 'DOCUMENT_ALREADY_VERIFIED'
                  ? 'A verified document cannot be withdrawn. Ask an administrator if it must be replaced.'
                  : error.code === 'INVALID_EXPIRY'
                    ? 'The expiry date cannot be before the issue date.'
                    : error.detail
            }
          />
        </div>
      ) : null}

      {documents.length === 0 ? (
        <p className="mt-3 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-6 text-center text-sm text-stone-500">
          No documents attached to this order yet.
        </p>
      ) : (
        <ul className="mt-3 divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
          {documents.map((doc) => {
            // Withdraw is offered only to whoever uploaded it, and only while it is unverified:
            // once verified it is the evidence the review rested on, and the server keeps it.
            const mine = userId !== null && doc.uploadedByUserId === userId
            const canWithdraw = mine && doc.status !== DOCUMENT_STATUS.Verified

            return (
              <li key={doc.id} className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="font-medium">{tradeDocumentLabel(doc.docType)}</p>
                    <DocumentStatusPill status={doc.status} />
                  </div>
                  <p className="mt-0.5 truncate text-sm text-stone-500">
                    {doc.fileName ?? 'No file name'}
                    {doc.documentReference ? ` · Ref ${doc.documentReference}` : ''}
                  </p>
                  <p className="mt-0.5 text-xs text-stone-400">
                    {[
                      doc.uploadedByDisplayName ? `Uploaded by ${doc.uploadedByDisplayName}` : null,
                      formatDate(doc.issuedOnUtc) ? `Issued ${formatDate(doc.issuedOnUtc)}` : null,
                      formatDate(doc.expiresOnUtc) ? `Expires ${formatDate(doc.expiresOnUtc)}` : null,
                      doc.fileSizeBytes ? fileSize(doc.fileSizeBytes) : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </p>
                  {doc.reviewNotes ? (
                    <p
                      className={`mt-2 rounded-lg px-3 py-2 text-xs ${
                        doc.status === DOCUMENT_STATUS.Rejected
                          ? 'bg-red-50 text-red-800'
                          : 'bg-stone-50 text-stone-600'
                      }`}
                    >
                      <span className="font-semibold">Reviewer:</span> {doc.reviewNotes}
                    </p>
                  ) : null}
                </div>

                <div className="flex shrink-0 items-center gap-3">
                  {doc.fileUrl ? (
                    <a
                      href={doc.fileUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="text-sm font-medium text-brand-700 hover:underline"
                    >
                      View
                    </a>
                  ) : null}
                  {canWithdraw ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={withdrawingId !== null}
                      onClick={() => void handleWithdraw(doc.id)}
                    >
                      {withdrawingId === doc.id ? 'Withdrawing…' : 'Withdraw'}
                    </Button>
                  ) : null}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {canUpload ? (
        <form onSubmit={handleUpload} className="mt-5 rounded-xl border border-stone-200 bg-stone-50/60 p-4">
          <h3 className="text-sm font-semibold text-stone-700">Attach a document</h3>
          <p className="mt-1 text-xs text-stone-500">
            Goes straight into the review queue. It is not treated as valid until an
            administrator verifies it. PDF, PNG or JPG, up to 10 MB.
          </p>

          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <SelectField
              label="Document type"
              id="doc-type"
              options={TRADE_DOCUMENT_TYPE_LABELS}
              values={TRADE_DOCUMENT_TYPE_VALUES}
              value={docType}
              onChange={(e) => setDocType(e.target.value)}
            />
            <div>
              <label htmlFor="doc-file" className="mb-1.5 block text-xs font-medium text-stone-700">
                File (PDF, PNG, JPG — max 10 MB)
              </label>
              <input
                id="doc-file"
                type="file"
                accept=".pdf,.png,.jpg,.jpeg"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
              />
            </div>
            <div>
              <label htmlFor="doc-ref" className="mb-1.5 block text-xs font-medium text-stone-700">
                Reference (optional)
              </label>
              <input
                id="doc-ref"
                type="text"
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                placeholder="INV-2026-0042"
                maxLength={100}
                className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="doc-issued" className="mb-1.5 block text-xs font-medium text-stone-700">
                  Issued (optional)
                </label>
                <input
                  id="doc-issued"
                  type="date"
                  value={issuedOn}
                  onChange={(e) => setIssuedOn(e.target.value)}
                  className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                />
              </div>
              <div>
                <label htmlFor="doc-expires" className="mb-1.5 block text-xs font-medium text-stone-700">
                  Expires (optional)
                </label>
                <input
                  id="doc-expires"
                  type="date"
                  value={expiresOn}
                  onChange={(e) => setExpiresOn(e.target.value)}
                  className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
                />
              </div>
            </div>
          </div>

          {expiryBeforeIssue ? (
            <p className="mt-3 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
              The expiry date is before the issue date. The server will reject that.
            </p>
          ) : null}

          <div className="mt-3">
            <Button type="submit" disabled={!file || uploading || expiryBeforeIssue}>
              {uploading ? 'Uploading…' : 'Upload document'}
            </Button>
          </div>
        </form>
      ) : isClosed ? (
        <p className="mt-5 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-4 text-sm text-stone-500">
          This order is {order.status === ORDER_STATUS.Cancelled ? 'cancelled' : 'complete'}, so it
          is closed to new documents.
        </p>
      ) : null}
    </section>
  )
}
