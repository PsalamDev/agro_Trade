import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { Card, EmptyState, ErrorAlert, Field, KeyValueEditor, SelectField, SubmitButton, TagInput, TextArea } from '../components/ui'
import { createProduct, deleteProductImage, getBusiness, getCategoryTree, getMyBusinesses, getProduct, updateProduct, uploadProductImage } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { COMPANY_ROLE } from '../types/auth'
import { INCOTERMS, LISTING_STATUS, LISTING_STATUSES, PRICE_UNITS, labelOf } from '../types/trade'
import type { BusinessSummaryDto, CategoryDto, ProductImageDto } from '../types/trade'

export default function ProductFormPage() {
  const { id } = useParams<{ id: string }>()
  const { user } = useAuth()
  const navigate = useNavigate()
  const isEdit = Boolean(id)
  const isCompany = (user?.roles ?? []).includes(COMPANY_ROLE)

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
  const [images, setImages] = useState<ProductImageDto[]>([])
  const [uploadFile, setUploadFile] = useState<File | null>(null)
  const [makePrimary, setMakePrimary] = useState(false)
  const [uploadPending, setUploadPending] = useState(false)
  const [uploadError, setUploadError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)

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
        setImages(product.images)
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

  async function refreshImages() {
    if (!id) return
    const updated = await getProduct(id)
    setImages(updated.images)
  }

  async function handleUpload() {
    if (!id || !uploadFile) return
    setUploadPending(true)
    setUploadError(null)
    try {
      await uploadProductImage(id, uploadFile, makePrimary)
      setUploadFile(null)
      setMakePrimary(false)
      await refreshImages()
    } catch (err) {
      setUploadError(problemFrom(err))
    } finally {
      setUploadPending(false)
    }
  }

  async function handleDeleteImage(imageId: string) {
    if (!id) return
    setUploadPending(true)
    setUploadError(null)
    try {
      await deleteProductImage(id, imageId)
      await refreshImages()
    } catch (err) {
      setUploadError(problemFrom(err))
    } finally {
      setUploadPending(false)
    }
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

  if (!isEdit && !isCompany) {
    return (
      <div className="min-h-screen">
        <AppHeader />
        <main className="mx-auto max-w-3xl px-4 py-10">
          <Link to="/my/products" className="text-sm font-medium text-brand-700 hover:underline">
            ← Back
          </Link>
          <div className="mt-6">
            <EmptyState
              title="Export listings are managed by the platform"
              body="Only the Achievers Global Consult supplier account can publish listings. Register to trade as a buyer/importer and publish RFQs instead."
            />
          </div>
        </main>
      </div>
    )
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
              <CountrySelect
                id="prod-origin"
                label="Origin country"
                value={originCountry}
                onChange={setOriginCountry}
                required
                hint="Search by country name — the ISO-2 code (e.g. KE) is generated automatically."
              />
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

          {isEdit && id ? (
            <Card>
              <h2 className="text-lg font-semibold">Photos</h2>
              <p className="mt-1 text-sm text-stone-500">
                The cover photo appears on marketplace cards. Buyers can browse all photos on the
                listing page.
              </p>

              {images.length > 0 ? (
                <div className="mt-4 grid grid-cols-3 gap-3 sm:grid-cols-4">
                  {images.map((img) => (
                    <div key={img.id} className="group relative overflow-hidden rounded-lg border border-stone-200">
                      <img src={img.url} alt={img.altText ?? ''} className="h-20 w-full object-cover" />
                      <div className="absolute inset-x-0 bottom-0 flex items-center justify-between gap-1 bg-gradient-to-t from-black/60 to-transparent p-1.5">
                        {img.isPrimary ? (
                          <span className="rounded-full bg-brand-500 px-2 py-0.5 text-[10px] font-semibold text-white">
                            Cover
                          </span>
                        ) : null}
                        <button
                          type="button"
                          onClick={() => void handleDeleteImage(img.id)}
                          disabled={uploadPending}
                          className="ml-auto rounded-full bg-black/50 px-2 py-0.5 text-[10px] font-semibold text-white transition hover:bg-red-600 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                          Delete
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-4 text-sm text-stone-500">
                  No photos yet. Add one below — a photo helps buyers trust the listing.
                </p>
              )}

              <div className="mt-4 space-y-3 rounded-lg bg-stone-50 p-4">
                <label className="flex items-center gap-2 text-sm font-medium">
                  <input
                    type="file"
                    accept="image/*"
                    className="block w-full text-sm text-stone-700 file:mr-3 file:rounded-md file:border-0 file:bg-brand-600 file:px-3 file:py-1.5 file:text-sm file:font-semibold file:text-white hover:file:bg-brand-700"
                    onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                  />
                </label>
                <label className="flex items-center gap-2 text-sm text-stone-700">
                  <input
                    type="checkbox"
                    checked={makePrimary}
                    onChange={(e) => setMakePrimary(e.target.checked)}
                    className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
                  />
                  Set as cover photo
                </label>
                {uploadError ? <ErrorAlert title={uploadError.detail} /> : null}
                <button
                  type="button"
                  onClick={() => void handleUpload()}
                  disabled={!uploadFile || uploadPending}
                  className="rounded-md bg-brand-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {uploadPending ? 'Uploading…' : 'Upload photo'}
                </button>
              </div>
            </Card>
          ) : null}

          <SubmitButton type="submit" pending={pending}>
            {isEdit ? 'Save listing' : 'Create listing'}
          </SubmitButton>
        </form>
      </main>
    </div>
  )
}