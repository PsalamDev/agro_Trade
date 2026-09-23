import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { Card, ErrorAlert, Field, KeyValueEditor, SelectField, SubmitButton, TagInput, TextArea } from '../components/ui'
import { createProduct, getBusiness, getCategoryTree, getMyBusinesses, getProduct, updateProduct } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { INCOTERMS, LISTING_STATUS, LISTING_STATUSES, PRICE_UNITS, labelOf } from '../types/trade'
import type { BusinessSummaryDto, CategoryDto } from '../types/trade'

export default function ProductFormPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const isEdit = Boolean(id)

  const [categories, setCategories] = useState<CategoryDto[]>([])
  const [businesses, setBusinesses] = useState<BusinessSummaryDto[]>([])
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [hsCode, setHsCode] = useState('')
  const [originCountry, setOriginCountry] = useState('')
  const [portOfLoading, setPortOfLoading] = useState('')
  const [price, setPrice] = useState('')
  const [priceUnit, setPriceUnit] = useState('1')
  const [currency, setCurrency] = useState('USD')
  const [availableQuantity, setAvailableQuantity] = useState('')
  const [minimumOrderQuantity, setMinimumOrderQuantity] = useState('')
  const [incoterms, setIncoterms] = useState('5')
  const [leadTimeDays, setLeadTimeDays] = useState('')
  const [paymentTermsOffered, setPaymentTermsOffered] = useState<string[]>([])
  const [qualitySpecs, setQualitySpecs] = useState<Record<string, string>>({})
  const [packagingOptions, setPackagingOptions] = useState<string[]>([])
  const [certifications, setCertifications] = useState<string[]>([])
  const [listingStatus, setListingStatus] = useState(String(LISTING_STATUS.Active))
  const [businessId, setBusinessId] = useState('')

  const [loaded, setLoaded] = useState(false)
  const [isOwner, setIsOwner] = useState(false)

  useEffect(() => {
    getCategoryTree().then(setCategories).catch(() => undefined)
  }, [])

  useEffect(() => {
    let cancelled = false
    getMyBusinesses()
      .then((list) => {
        if (!cancelled) setBusinesses(list)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (!id) {
      setLoaded(true)
      return
    }
    let cancelled = false
    getProduct(id)
      .then(async (product) => {
        if (cancelled) return
        let owned = false
        try {
          const biz = await getBusiness(product.sellerBusinessId)
          owned = biz.isOwner
        } catch {
          owned = false
        }
        if (cancelled) return
        setName(product.name)
        setDescription(product.description ?? '')
        setCategoryId(product.categoryId)
        setHsCode(product.hsCode ?? '')
        setOriginCountry(product.originCountry)
        setPortOfLoading(product.portOfLoading ?? '')
        setPrice(String(product.price))
        setPriceUnit(String(product.priceUnit))
        setCurrency(product.currency)
        setAvailableQuantity(String(product.availableQuantity))
        setMinimumOrderQuantity(String(product.minimumOrderQuantity))
        setIncoterms(product.incoterms === null || product.incoterms === undefined ? '5' : String(product.incoterms))
        setLeadTimeDays(product.leadTimeDays === null || product.leadTimeDays === undefined ? '' : String(product.leadTimeDays))
        setPaymentTermsOffered(product.paymentTermsOffered)
        setQualitySpecs(product.qualitySpecs)
        setPackagingOptions(product.packagingOptions)
        setCertifications(product.certifications)
        setListingStatus(String(product.listingStatus))
        setIsOwner(owned)
        setLoaded(true)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
        setLoaded(true)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const leafCategories = useMemo(
    () => categories.flatMap(({ children }) => children.map((child) => child)),
    [categories],
  )

  if (!loaded) {
    return (
      <div className="min-h-screen">
        <AppHeader />
        <main className="mx-auto max-w-3xl px-4 py-10">
          <p className="animate-pulse text-sm text-stone-500">Loading listing…</p>
        </main>
      </div>
    )
  }

  if (isEdit && !isOwner) {
    return (
      <div className="min-h-screen">
        <AppHeader />
        <main className="mx-auto max-w-3xl px-4 py-10">
          <ErrorAlert title="You can only edit listings you own." />
          <Link to={`/products/${id}`} className="mt-4 inline-block text-sm font-medium text-brand-700 hover:underline">
            ← Back to listing
          </Link>
        </main>
      </div>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const payload = {
        name,
        categoryId,
        hsCode: hsCode || undefined,
        originCountry,
        portOfLoading: portOfLoading || undefined,
        price: Number(price),
        priceUnit: Number(priceUnit),
        currency,
        availableQuantity: Number(availableQuantity),
        minimumOrderQuantity: Number(minimumOrderQuantity),
        incoterms: Number(incoterms),
        leadTimeDays: leadTimeDays ? Number(leadTimeDays) : undefined,
        paymentTermsOffered,
        qualitySpecs,
        packagingOptions,
        certifications,
        listingStatus: Number(listingStatus),
      }
      const saved = isEdit && id ? await updateProduct(id, payload) : await createProduct(payload, businessId || undefined)
      navigate(`/products/${saved.slug || saved.id}`, { replace: true })
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-3xl px-4 py-10">
        <Link to={isEdit ? `/products/${id}` : '/my/products'} className="text-sm font-medium text-brand-700 hover:underline">
          ← Back
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">{isEdit ? 'Edit listing' : 'New export listing'}</h1>
        <p className="mt-1 text-sm text-stone-500">
          Listings published as Active require a verified business. Unverified sellers will see an error until then.
        </p>

        <form onSubmit={handleSubmit} className="mt-8 space-y-6">
          <Card>
            <h2 className="text-lg font-semibold">Product</h2>
            <div className="mt-4 space-y-4">
              <ErrorAlert title={error?.detail} fieldErrors={error?.fieldErrors} />
              {!isEdit && businesses.length > 0 ? (
                <SelectField
                  label="Selling business"
                  id="prod-business"
                  options={businesses.map((b) => `${b.tradingName} (${labelOf(['Sole proprietor', 'Partnership', 'Limited company', 'Cooperative', 'NGO', 'Other'], b.businessType)})`)}
                  values={businesses.map((b) => b.id)}
                  value={businessId}
                  onChange={(e) => setBusinessId(e.target.value)}
                />
              ) : null}
              <Field label="Name *" id="prod-name" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Arabica Green Beans AA" />
              <TextArea label="Description" id="prod-description" maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Grade, moisture, defects, roast profile, packaging…" />
              <div className="grid gap-4 sm:grid-cols-2">
                <SelectField
                  label="Category *"
                  id="prod-category"
                  options={leafCategories.map((c) => c.name)}
                  values={leafCategories.map((c) => c.id)}
                  value={categoryId}
                  onChange={(e) => setCategoryId(e.target.value)}
                />
                <Field label="HS code" id="prod-hs" maxLength={10} value={hsCode} onChange={(e) => setHsCode(e.target.value)} placeholder="0901.11" />
              </div>
            </div>
          </Card>

          <Card>
            <h2 className="text-lg font-semibold">Trade terms</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label="Origin country (ISO-2) *" id="prod-origin" required maxLength={2} value={originCountry} onChange={(e) => setOriginCountry(e.target.value.toUpperCase())} placeholder="KE" />
              <Field label="Port of loading" id="prod-pol" maxLength={120} value={portOfLoading} onChange={(e) => setPortOfLoading(e.target.value)} placeholder="Mombasa" />
              <Field label="Price *" id="prod-price" type="number" min="0" step="any" required value={price} onChange={(e) => setPrice(e.target.value)} />
              <SelectField label="Price unit *" id="prod-price-unit" options={PRICE_UNITS} value={priceUnit} onChange={(e) => setPriceUnit(e.target.value)} />
              <Field label="Currency (ISO-3) *" id="prod-currency" required maxLength={3} value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
              <SelectField label="Incoterm" id="prod-incoterm" options={INCOTERMS} value={incoterms} onChange={(e) => setIncoterms(e.target.value)} />
              <Field label="Available quantity *" id="prod-available" type="number" min="0" step="any" required value={availableQuantity} onChange={(e) => setAvailableQuantity(e.target.value)} />
              <Field label="Minimum order quantity *" id="prod-moq" type="number" min="0" step="any" required value={minimumOrderQuantity} onChange={(e) => setMinimumOrderQuantity(e.target.value)} />
              <Field label="Lead time (days)" id="prod-lead" type="number" min="0" value={leadTimeDays} onChange={(e) => setLeadTimeDays(e.target.value)} />
              <SelectField label="Listing status" id="prod-status" options={LISTING_STATUSES} value={listingStatus} onChange={(e) => setListingStatus(e.target.value)} />
            </div>
          </Card>

          <Card>
            <h2 className="text-lg font-semibold">Quality &amp; logistics</h2>
            <div className="mt-4 space-y-5">
              <TagInput label="Payment terms offered" id="prod-payment" value={paymentTermsOffered} onChange={setPaymentTermsOffered} />
              <KeyValueEditor label="Quality specifications" id="prod-specs" value={qualitySpecs} onChange={setQualitySpecs} placeholder="e.g. moisture" />
              <TagInput label="Packaging options" id="prod-packaging" value={packagingOptions} onChange={setPackagingOptions} />
              <TagInput label="Certifications" id="prod-certs" value={certifications} onChange={setCertifications} />
            </div>
          </Card>

          <SubmitButton type="submit" pending={pending}>
            {isEdit ? 'Save listing' : 'Create listing'}
          </SubmitButton>
        </form>
      </main>
    </div>
  )
}