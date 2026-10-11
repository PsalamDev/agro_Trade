import { useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import { Button, Card, ErrorAlert, Field, PageHeader, Spinner, TextArea } from '../components/ui'
import { countryName } from '../lib/trade'
import { getMyLogisticsProvider, upsertMyLogisticsProvider } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { LOGISTICS_SERVICE_OPTIONS, SHIPMENT_MODES, modeLabel } from '../types/logistics'
import type { UpsertLogisticsProviderRequest } from '../types/logistics'

interface FormState {
  companyName: string
  description: string
  homeCountry: string
  contactEmail: string
  contactPhone: string
  website: string
  modes: number[]
  services: number[]
  coverage: string[]
  isActive: boolean
}

const EMPTY: FormState = {
  companyName: '',
  description: '',
  homeCountry: '',
  contactEmail: '',
  contactPhone: '',
  website: '',
  modes: [],
  services: [],
  coverage: [],
  isActive: true,
}

export default function LogisticsProfilePage() {
  const [form, setForm] = useState<FormState>(EMPTY)
  const [loading, setLoading] = useState(true)
  const [loaded, setLoaded] = useState(false)
  const [pending, setPending] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(
    null,
  )
  const [coveragePick, setCoveragePick] = useState('')

  useEffect(() => {
    let cancelled = false
    getMyLogisticsProvider()
      .then((provider) => {
        if (cancelled) return
        if (provider) {
          setLoaded(true)
          setForm({
            companyName: provider.companyName,
            description: provider.description ?? '',
            homeCountry: provider.homeCountry,
            contactEmail: provider.contactEmail ?? '',
            contactPhone: provider.contactPhone ?? '',
            website: provider.website ?? '',
            modes: provider.modes,
            services: provider.services,
            coverage: provider.coverage,
            isActive: provider.isActive,
          })
        }
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  function toggle(list: number[], value: number): number[] {
    return list.includes(value) ? list.filter((item) => item !== value) : [...list, value]
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setSaved(false)
    setPending(true)
    try {
      const request: UpsertLogisticsProviderRequest = {
        companyName: form.companyName.trim(),
        description: form.description.trim() || null,
        homeCountry: form.homeCountry,
        contactEmail: form.contactEmail.trim() || null,
        contactPhone: form.contactPhone.trim() || null,
        website: form.website.trim() || null,
        modes: form.modes,
        services: form.services,
        coverage: form.coverage,
        isActive: form.isActive,
      }
      const savedProvider = await upsertMyLogisticsProvider(request)
      setLoaded(true)
      setForm((prev) => ({ ...prev, modes: savedProvider.modes, services: savedProvider.services, coverage: savedProvider.coverage }))
      setSaved(true)
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
        <PageHeader
          eyebrow="Logistics"
          title={loaded ? 'Your provider profile' : 'List your logistics business'}
          subtitle="Buyers and sellers find you here and book you directly against their orders. Keep this current — inactive profiles leave the directory."
        />

        {loading ? (
          <div className="mt-8">
            <Spinner label="Loading your profile." />
          </div>
        ) : (
          <form onSubmit={(event) => void submit(event)} className="mt-6 space-y-5">
            <Card>
              <h2 className="text-base font-semibold">Business</h2>
              <div className="mt-4 space-y-4">
                <Field
                  label="Company name"
                  id="lsp-name"
                  required
                  value={form.companyName}
                  onChange={(e) => setForm({ ...form, companyName: e.target.value })}
                />
                <TextArea
                  label="What you do"
                  id="lsp-description"
                  rows={4}
                  placeholder="Routes, equipment, specialities, typical lead times…"
                  value={form.description}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                />
                <CountrySelect
                  id="lsp-home"
                  label="Home country"
                  required
                  value={form.homeCountry}
                  onChange={(code) => setForm({ ...form, homeCountry: code })}
                />
              </div>
            </Card>

            <Card>
              <h2 className="text-base font-semibold">Contact</h2>
              <p className="mt-1 text-xs text-stone-500">Shown only to signed-in users on your profile page.</p>
              <div className="mt-4 grid gap-4 sm:grid-cols-2">
                <Field
                  label="Contact email"
                  id="lsp-email"
                  type="email"
                  value={form.contactEmail}
                  onChange={(e) => setForm({ ...form, contactEmail: e.target.value })}
                />
                <Field
                  label="Contact phone"
                  id="lsp-phone"
                  value={form.contactPhone}
                  onChange={(e) => setForm({ ...form, contactPhone: e.target.value })}
                />
                <div className="sm:col-span-2">
                  <Field
                    label="Website"
                    id="lsp-website"
                    type="url"
                    placeholder="https://"
                    value={form.website}
                    onChange={(e) => setForm({ ...form, website: e.target.value })}
                  />
                </div>
              </div>
            </Card>

            <Card>
              <h2 className="text-base font-semibold">Modes</h2>
              <p className="mt-1 text-xs text-stone-500">The transport modes you offer.</p>
              <div className="mt-3 flex flex-wrap gap-x-5 gap-y-2">
                {SHIPMENT_MODES.map((_, index) => (
                  <label key={index} className="inline-flex items-center gap-2 text-sm text-stone-700">
                    <input
                      type="checkbox"
                      className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
                      checked={form.modes.includes(index)}
                      onChange={() => setForm({ ...form, modes: toggle(form.modes, index) })}
                    />
                    {modeLabel(index)}
                  </label>
                ))}
              </div>
            </Card>

            <Card>
              <h2 className="text-base font-semibold">Services</h2>
              <p className="mt-1 text-xs text-stone-500">Everything beyond moving the box.</p>
              <div className="mt-3 grid gap-x-5 gap-y-2 sm:grid-cols-2">
                {LOGISTICS_SERVICE_OPTIONS.map((option) => (
                  <label key={option.value} className="inline-flex items-center gap-2 text-sm text-stone-700">
                    <input
                      type="checkbox"
                      className="h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
                      checked={form.services.includes(option.value)}
                      onChange={() => setForm({ ...form, services: toggle(form.services, option.value) })}
                    />
                    {option.label}
                  </label>
                ))}
              </div>
            </Card>

            <Card>
              <h2 className="text-base font-semibold">Coverage</h2>
              <p className="mt-1 text-xs text-stone-500">
                The countries you can serve. Buyers filter the directory by these.
              </p>
              <div className="mt-3">
                <CountrySelect
                  id="lsp-coverage"
                  label="Add a country"
                  value={coveragePick}
                  onChange={setCoveragePick}
                  allowClear
                  placeholder="Search for a country…"
                />
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  className="mt-2"
                  disabled={!coveragePick || form.coverage.includes(coveragePick)}
                  onClick={() => {
                    setForm({ ...form, coverage: [...form.coverage, coveragePick] })
                    setCoveragePick('')
                  }}
                >
                  Add country
                </Button>
              </div>
              {form.coverage.length ? (
                <div className="mt-3 flex flex-wrap gap-1.5">
                  {form.coverage.map((code) => (
                    <span
                      key={code}
                      className="inline-flex items-center gap-1 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-medium text-brand-800"
                    >
                      {countryName(code)}
                      <button
                        type="button"
                        aria-label={`Remove ${countryName(code)}`}
                        className="text-brand-600 hover:text-brand-900"
                        onClick={() => setForm({ ...form, coverage: form.coverage.filter((c) => c !== code) })}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-stone-500">No coverage countries yet.</p>
              )}
            </Card>

            <Card>
              <label className="flex items-start gap-3">
                <input
                  type="checkbox"
                  className="mt-0.5 h-4 w-4 rounded border-stone-300 text-brand-600 focus:ring-brand-500"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                />
                <span>
                  <span className="block text-sm font-medium text-stone-800">Accepting bookings</span>
                  <span className="block text-xs text-stone-500">
                    Turn this off to leave the directory without losing your profile or booking history.
                  </span>
                </span>
              </label>
            </Card>

            {error ? (
              <ErrorAlert title="Could not save your profile" detail={error.detail} fieldErrors={error.fieldErrors} />
            ) : null}
            {saved ? (
              <p className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-800">
                Profile saved.
              </p>
            ) : null}

            <div className="flex items-center gap-3">
              <Button type="submit" disabled={pending}>
                {pending ? 'Saving…' : loaded ? 'Save profile' : 'Publish profile'}
              </Button>
            </div>
          </form>
        )}
      </main>
    </div>
  )
}
