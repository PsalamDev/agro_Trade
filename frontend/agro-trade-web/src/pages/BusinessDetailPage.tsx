import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerificationPill, VerifiedBadge } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Field, SelectField, Spinner, SubmitButton, TextArea, TagInput } from '../components/ui'
import {
  getBusiness,
  getBusinessDocuments,
  getVerification,
  setTradeCountries,
  submitForVerification,
  updateBusiness,
  uploadBusinessDocument,
} from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc, formatUtcDate } from '../lib/format'
import { DOCUMENT_TYPE, DOCUMENT_TYPES, VERIFICATION_STATUS, labelOf } from '../types/trade'
import type { BusinessDetailDto, BusinessDocumentDto, VerificationStateDto } from '../types/trade'

export default function BusinessDetailPage() {
  const { id } = useParams()
  const [business, setBusiness] = useState<BusinessDetailDto | null>(null)
  const [documents, setDocuments] = useState<BusinessDocumentDto[] | null>(null)
  const [verification, setVerification] = useState<VerificationStateDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [actionError, setActionError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)
  const [pending, setPending] = useState(false)

  const [tradingName, setTradingName] = useState('')
  const [website, setWebsite] = useState('')
  const [description, setDescription] = useState('')
  const [exportsTo, setExportsTo] = useState<string[]>([])
  const [importsFrom, setImportsFrom] = useState<string[]>([])
  const [docType, setDocType] = useState(String(DOCUMENT_TYPE.BusinessRegistration))
  const [docFile, setDocFile] = useState<File | null>(null)

  const load = useCallback(async () => {
    if (!id) return
    setLoading(true)
    setError(null)
    try {
      const [biz, docs, ver] = await Promise.all([
        getBusiness(id),
        getBusinessDocuments(id),
        getVerification(id),
      ])
      setBusiness(biz)
      setDocuments(docs)
      setVerification(ver)
      setTradingName(biz.tradingName)
      setWebsite(biz.website ?? '')
      setDescription(biz.description ?? '')
      setExportsTo(biz.exportsTo)
      setImportsFrom(biz.importsFrom)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    void load()
  }, [load])

  async function handleUpdate(event: FormEvent) {
    event.preventDefault()
    if (!id) return
    setPending(true)
    setActionError(null)
    try {
      const [updated] = await Promise.all([
        updateBusiness(id, { tradingName, website: website || undefined, description: description || undefined }),
        setTradeCountries(id, { exportsTo, importsFrom }),
      ])
      setBusiness((b) => (b ? { ...b, ...updated } : b))
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  async function handleUpload(event: FormEvent) {
    event.preventDefault()
    if (!id || !docFile) return
    setPending(true)
    setActionError(null)
    try {
      await uploadBusinessDocument(id, Number(docType), docFile)
      setDocFile(null)
      setDocuments(await getBusinessDocuments(id))
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  async function handleSubmitVerification() {
    if (!id) return
    setPending(true)
    setActionError(null)
    try {
      setVerification(await submitForVerification(id))
      setBusiness(await getBusiness(id))
    } catch (err) {
      setActionError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-5xl px-4 py-10">
        <Link to="/business" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to business profiles
        </Link>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Business not available" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : business ? (
          <div className="mt-6 grid gap-6 lg:grid-cols-3">
            <div className="space-y-6 lg:col-span-2">
              <Card>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h1 className="text-2xl font-bold tracking-tight">{business.tradingName}</h1>
                    <p className="mt-1 text-sm text-stone-500">
                      {business.legalName} · {labelOf(['Sole proprietor', 'Partnership', 'Limited company', 'Cooperative', 'NGO', 'Other'], business.businessType)}
                    </p>
                  </div>
                  <div className="flex flex-col items-end gap-1.5">
                    <VerifiedBadge visible={business.isVerifiedBadgeVisible} size="lg" />
                    {verification ? <VerificationPill status={verification.status} /> : null}
                  </div>
                </div>

                <form onSubmit={handleUpdate} className="mt-6 space-y-4 border-t border-stone-100 pt-6">
                  <h2 className="text-lg font-semibold">Profile</h2>
                  <ErrorAlert title={actionError?.detail} fieldErrors={actionError?.fieldErrors} />
                  <Field label="Trading name" id="biz-edit-trading" maxLength={250} value={tradingName} onChange={(e) => setTradingName(e.target.value)} />
                  <Field label="Website" id="biz-edit-website" maxLength={300} value={website} onChange={(e) => setWebsite(e.target.value)} />
                  <TextArea label="Description" id="biz-edit-description" maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} />
                  <SubmitButton type="submit" pending={pending && actionError === null}>
                    Save profile &amp; trade countries
                  </SubmitButton>
                </form>
              </Card>

              <Card>
                <h2 className="text-lg font-semibold">Trade countries</h2>
                <p className="mt-1 text-sm text-stone-500">
                  ISO-3166 alpha-2 codes. These drive marketplace reachability filters (exports-to / imports-from).
                </p>
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <TagInput label="Exports to" id="biz-exports" value={exportsTo} onChange={setExportsTo} />
                  <TagInput label="Imports from" id="biz-imports" value={importsFrom} onChange={setImportsFrom} />
                </div>
              </Card>

              <Card>
                <h2 className="text-lg font-semibold">Verification documents</h2>
                <p className="mt-1 text-sm text-stone-500">
                  Upload business registration plus any certifications. Verifying requires at least one document.
                </p>
                <form onSubmit={handleUpload} className="mt-4 space-y-4">
                  <ErrorAlert title={actionError?.detail} fieldErrors={actionError?.fieldErrors} />
                  <div className="grid items-end gap-4 sm:grid-cols-2">
                    <SelectField
                      label="Document type"
                      id="biz-doc-type"
                      options={DOCUMENT_TYPES}
                      values={Object.values(DOCUMENT_TYPE)}
                      value={docType}
                      onChange={(e) => setDocType(e.target.value)}
                    />
                    <Field
                      label="File (PDF, PNG, JPG — max 10 MB)"
                      id="biz-doc-file"
                      type="file"
                      accept=".pdf,.png,.jpg,.jpeg"
                      onChange={(e) => setDocFile(e.target.files?.[0] ?? null)}
                    />
                  </div>
                  <button
                    type="submit"
                    disabled={!docFile || pending}
                    className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    Upload document
                  </button>
                </form>

                {documents === null ? (
                  <div className="mt-4">
                    <Spinner label="Loading documents…" />
                  </div>
                ) : documents.length === 0 ? (
                  <div className="mt-4">
                    <EmptyState title="No documents yet" body="Upload your business registration to get started." />
                  </div>
                ) : (
                  <ul className="mt-4 divide-y divide-stone-100 rounded-lg border border-stone-200">
                    {documents.map((doc) => (
                      <li key={doc.id} className="flex items-center justify-between gap-3 px-3 py-2.5 text-sm">
                        <div className="min-w-0">
                          <p className="truncate font-medium text-stone-800">
                            {labelOf(DOCUMENT_TYPES, doc.docType)}
                          </p>
                          <p className="truncate text-xs text-stone-500">{doc.fileName}</p>
                        </div>
                        <div className="flex shrink-0 items-center gap-3">
                          <span className="text-xs text-stone-400">{formatUtcDate(doc.uploadedAtUtc)}</span>
                          <a href={doc.fileUrl} target="_blank" rel="noreferrer" className="font-medium text-brand-700 hover:underline">
                            View
                          </a>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            </div>

            <div className="space-y-6">
              <Card>
                <h2 className="text-lg font-semibold">Verification status</h2>
                {verification ? (
                  <div className="mt-4 space-y-4 text-sm">
                    <div className="flex items-center gap-2">
                      <VerificationPill status={verification.status} />
                      <VerifiedBadge visible={verification.isBadgeVisible} />
                    </div>
                    {verification.verifiedLevel ? (
                      <p>
                        <span className="text-xs font-semibold uppercase tracking-wide text-stone-500">Level</span>
                        <br />
                        {verification.verifiedLevel}
                      </p>
                    ) : null}
                    {verification.requestedAtUtc ? (
                      <p>
                        <span className="text-xs font-semibold uppercase tracking-wide text-stone-500">Requested</span>
                        <br />
                        {formatUtc(verification.requestedAtUtc)}
                      </p>
                    ) : null}
                    {verification.reviewedAtUtc ? (
                      <p>
                        <span className="text-xs font-semibold uppercase tracking-wide text-stone-500">Reviewed</span>
                        <br />
                        {formatUtc(verification.reviewedAtUtc)}
                      </p>
                    ) : null}
                    {verification.notes ? (
                      <p>
                        <span className="text-xs font-semibold uppercase tracking-wide text-stone-500">Review notes</span>
                        <br />
                        {verification.notes}
                      </p>
                    ) : null}

                    {verification.status === VERIFICATION_STATUS.Unverified || verification.status === VERIFICATION_STATUS.Rejected ? (
                      <button
                        type="button"
                        disabled={pending || (documents?.length ?? 0) === 0}
                        onClick={() => void handleSubmitVerification()}
                        className="w-full rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        {documents?.length === 0 ? 'Upload a document to submit' : 'Submit for verification'}
                      </button>
                    ) : null}

                    {verification.status === VERIFICATION_STATUS.Pending ? (
                      <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-800">
                        Your submission is in the review queue. You’ll be notified when it’s approved or rejected.
                      </p>
                    ) : null}
                  </div>
                ) : null}
              </Card>

              <Card>
                <h2 className="text-lg font-semibold">What changes when verified?</h2>
                <ul className="mt-3 list-inside list-disc space-y-2 text-sm text-stone-600">
                  <li>Your listings can go <span className="font-medium">Active</span> on the marketplace.</li>
                  <li>A verified badge appears beside your company name.</li>
                  <li>Verified sellers can quote on buyer RFQs.</li>
                </ul>
              </Card>
            </div>
          </div>
        ) : null}
      </main>
    </div>
  )
}