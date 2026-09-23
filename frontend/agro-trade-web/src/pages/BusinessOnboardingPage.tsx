import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { Card, ErrorAlert, Field, SelectField, SubmitButton, TextArea } from '../components/ui'
import { createBusiness, getCategoryTree } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { BUSINESS_TYPES } from '../types/trade'
import type { CategoryDto } from '../types/trade'

export default function BusinessOnboardingPage() {
  const navigate = useNavigate()
  const [categories, setCategories] = useState<CategoryDto[]>([])
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(null)

  const [legalName, setLegalName] = useState('')
  const [tradingName, setTradingName] = useState('')
  const [registrationNumber, setRegistrationNumber] = useState('')
  const [businessType, setBusinessType] = useState('2')
  const [categoryFocusId, setCategoryFocusId] = useState('')
  const [website, setWebsite] = useState('')
  const [description, setDescription] = useState('')

  useEffect(() => {
    getCategoryTree().then(setCategories).catch(() => undefined)
  }, [])

  const leafCategories = useMemo(
    () => categories.flatMap(({ children }) => children.map((child) => child)),
    [categories],
  )

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const business = await createBusiness({
        legalName,
        tradingName,
        registrationNumber: registrationNumber || undefined,
        businessType: Number(businessType),
        categoryFocusId: categoryFocusId || undefined,
        website: website || undefined,
        description: description || undefined,
      })
      navigate(`/business/${business.id}`)
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
        <Link to="/business" className="text-sm font-medium text-brand-700 hover:underline">
          ← Back to business profiles
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">Register your business</h1>
        <p className="mt-1 text-sm text-stone-500">
          Tell buyers who you are. After registering, add trade countries and verification documents to get verified.
        </p>

        <form onSubmit={handleSubmit} className="mt-8 space-y-6">
          <Card>
            <h2 className="text-lg font-semibold">Entity details</h2>
            <div className="mt-4 space-y-4">
              <Field label="Legal name *" id="biz-legal" required maxLength={250} value={legalName} onChange={(e) => setLegalName(e.target.value)} placeholder="e.g. Zane Produce Ltd" />
              <Field label="Trading name *" id="biz-trading" required maxLength={250} value={tradingName} onChange={(e) => setTradingName(e.target.value)} placeholder="e.g. Zane Produce KE" />
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Registration number" id="biz-reg" maxLength={100} value={registrationNumber} onChange={(e) => setRegistrationNumber(e.target.value)} />
                <SelectField label="Business type *" id="biz-type" options={BUSINESS_TYPES} value={businessType} onChange={(e) => setBusinessType(e.target.value)} />
              </div>
              <SelectField
                label="Category focus"
                id="biz-category"
                options={leafCategories.map((c) => c.name)}
                values={leafCategories.map((c) => c.id)}
                value={categoryFocusId}
                onChange={(e) => setCategoryFocusId(e.target.value)}
              />
              <Field label="Website" id="biz-website" type="url" maxLength={300} value={website} onChange={(e) => setWebsite(e.target.value)} placeholder="https://example.com" />
              <TextArea label="Description" id="biz-description" maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Who you are, what you export, capacity…" />
            </div>
          </Card>

          <ErrorAlert title={error?.detail} fieldErrors={error?.fieldErrors} />
          <SubmitButton type="submit" pending={pending}>
            Create business profile
          </SubmitButton>
        </form>
      </main>
    </div>
  )
}