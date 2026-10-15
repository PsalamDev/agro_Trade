import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { Card, ErrorAlert, Field, KeyValueEditor, SelectField, SubmitButton, TagInput, TextArea } from '../components/ui'
import { getCategoryTree, createRfq } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import type { CategoryDto } from '../types/trade'
import { INCOTERMS, PAYMENT_TERMS, PAYMENT_TERM, PRICE_UNITS, SHIPMENT_MODES } from '../types/trade'

export default function RfqPublishPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [categories, setCategories] = useState<CategoryDto[]>([])
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [quantity, setQuantity] = useState('')
  const [unit, setUnit] = useState('1')
  const [destinationCountry, setDestinationCountry] = useState('')
  const [destinationPortOrCity, setDestinationPortOrCity] = useState('')
  const [preferredIncoterm, setPreferredIncoterm] = useState('')
  const [transportMode, setTransportMode] = useState('')
  const [insuranceRequired, setInsuranceRequired] = useState(false)
  const [targetSpecs, setTargetSpecs] = useState<Record<string, string>>({})
  const [certifications, setCertifications] = useState<string[]>([])
  const [preferredCurrency, setPreferredCurrency] = useState('USD')
  const [paymentTermsPreferred, setPaymentTermsPreferred] = useState('')
  const [deliveryDeadline, setDeliveryDeadline] = useState('')
  const [budgetMin, setBudgetMin] = useState('')
  const [budgetMax, setBudgetMax] = useState('')
  const [expiresAt, setExpiresAt] = useState(() => {
    const d = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    return d.toISOString().slice(0, 10)
  })

  useEffect(() => {
    getCategoryTree().then(setCategories).catch(() => undefined)
  }, [])

  const leafCategories = useMemo(
    () => categories.flatMap(({ children }) => children.map((child) => child)),
    [categories],
  )

  if (!user) return null

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const rfq = await createRfq({
        title,
        description: description || undefined,
        categoryId: categoryId || undefined,
        quantity: Number(quantity),
        unit: Number(unit),
        destinationCountry,
        destinationPortOrCity: destinationPortOrCity || undefined,
        preferredIncoterm: preferredIncoterm ? Number(preferredIncoterm) : undefined,
        transportMode: transportMode ? Number(transportMode) : undefined,
        insuranceRequired,
        targetSpecs,
        certificationsRequired: certifications,
        preferredCurrency,
        paymentTermsPreferred: paymentTermsPreferred ? Number(paymentTermsPreferred) : undefined,
        deliveryDeadline: deliveryDeadline ? new Date(`${deliveryDeadline}T00:00:00Z`).toISOString() : undefined,
        budgetMin: budgetMin ? Number(budgetMin) : undefined,
        budgetMax: budgetMax ? Number(budgetMax) : undefined,
        expiresAt: new Date(`${expiresAt}T23:59:59Z`).toISOString(),
      })
      navigate(`/rfqs/${rfq.id}`, { replace: true })
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-3xl flex-1 px-4 py-10">
        <Link to="/rfqs" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to RFQ directory
        </Link>
        <h1 className="font-display mt-3 text-2xl font-bold tracking-tight">Publish a sourcing request</h1>
        <p className="mt-1 text-sm text-stone-500">
          Describe what you need, where it must land, and the trade terms that suit you. Verified suppliers will quote.
        </p>

        <form onSubmit={handleSubmit} className="mt-8 space-y-6">
          <Card>
            <h2 className="font-display text-lg font-semibold">Request</h2>
            <div className="mt-4 space-y-4">
              <Field label="Title *" id="rfq-title" required value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. White Maize Grade 1 — 100 MT for Lagos" maxLength={200} />
              <TextArea label="Description" id="rfq-description" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={2000} placeholder="Grade, moisture, impurities, bagging, delivery expectations…" />
              <SelectField
                label="Category"
                id="rfq-category"
                options={leafCategories.map((c) => c.name)}
                values={leafCategories.map((c) => c.id)}
                value={categoryId}
                onChange={(e) => setCategoryId(e.target.value)}
              />
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Quantity *" id="rfq-quantity" type="number" min="0" step="any" required value={quantity} onChange={(e) => setQuantity(e.target.value)} />
                <SelectField label="Unit *" id="rfq-unit" options={PRICE_UNITS} value={unit} onChange={(e) => setUnit(e.target.value)} />
              </div>
            </div>
          </Card>

          <Card>
            <h2 className="font-display text-lg font-semibold">Delivery</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <CountrySelect
                id="rfq-dest-country"
                label="Destination country"
                value={destinationCountry}
                onChange={setDestinationCountry}
                required
              />
              <Field label="Destination port or city" id="rfq-dest-port" value={destinationPortOrCity} onChange={(e) => setDestinationPortOrCity(e.target.value)} placeholder="Lagos / Apapa" />
              <SelectField label="Preferred incoterm" id="rfq-incoterm" options={INCOTERMS} value={preferredIncoterm} onChange={(e) => setPreferredIncoterm(e.target.value)} />
              <SelectField label="Transport mode" id="rfq-transport" options={SHIPMENT_MODES} value={transportMode} onChange={(e) => setTransportMode(e.target.value)} />
            </div>
            <label className="mt-4 flex items-center gap-2 text-sm font-medium text-stone-700">
              <input
                type="checkbox"
                checked={insuranceRequired}
                onChange={(e) => setInsuranceRequired(e.target.checked)}
                className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
              />
              Cargo insurance required
            </label>
          </Card>

          <Card>
            <h2 className="font-display text-lg font-semibold">Terms</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label="Preferred currency (ISO-3) *" id="rfq-currency" required maxLength={3} value={preferredCurrency} onChange={(e) => setPreferredCurrency(e.target.value.toUpperCase())} />
              <SelectField label="Payment terms preferred" id="rfq-payment" options={PAYMENT_TERMS} values={Object.values(PAYMENT_TERM)} value={paymentTermsPreferred} onChange={(e) => setPaymentTermsPreferred(e.target.value)} />
              <Field label="Delivery deadline" id="rfq-deadline" type="date" value={deliveryDeadline} onChange={(e) => setDeliveryDeadline(e.target.value)} />
              <Field label="RFQ expires *" id="rfq-expires" type="date" required value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
              <Field label="Budget min" id="rfq-budget-min" type="number" min="0" step="any" value={budgetMin} onChange={(e) => setBudgetMin(e.target.value)} />
              <Field label="Budget max" id="rfq-budget-max" type="number" min="0" step="any" value={budgetMax} onChange={(e) => setBudgetMax(e.target.value)} />
            </div>
          </Card>

          <Card>
            <h2 className="font-display text-lg font-semibold">Specs &amp; certifications</h2>
            <div className="mt-4 space-y-5">
              <KeyValueEditor label="Target specifications" id="rfq-specs" value={targetSpecs} onChange={setTargetSpecs} placeholder="e.g. grade" />
              <TagInput label="Certifications required" id="rfq-certs" value={certifications} onChange={setCertifications} />
            </div>
          </Card>

          <ErrorAlert title={error?.detail} fieldErrors={error?.fieldErrors} />
          <SubmitButton type="submit" pending={pending}>
            Publish RFQ
          </SubmitButton>
        </form>
      </main>
    </div>
  )
}