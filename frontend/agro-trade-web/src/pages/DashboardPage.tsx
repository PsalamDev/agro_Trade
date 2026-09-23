import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'

const SELLER_ROLES = ['Farmer', 'Supplier', 'AgroProcessor', 'Exporter', 'Distributor']
const BUYER_ROLES = ['Buyer', 'Importer', 'Distributor']

const quickActions = [
  {
    title: 'Marketplace',
    phase: 'Live · Phase 1',
    body: 'Browse verified export listings, filter by origin, destination, and price.',
    to: '/marketplace',
    cta: 'Explore supply',
  },
  {
    title: 'RFQ directory',
    phase: 'Live · Phase 1',
    body: 'Open importers’ sourcing requests. Verified suppliers quote with full trade terms.',
    to: '/rfqs',
    cta: 'Find requests',
  },
  {
    title: 'Messages & RFQs',
    phase: 'Live · Phase 1',
    body: 'Negotiation threads per RFQ, plus RFQ/quotation/verification notifications.',
    to: '/messages',
    cta: 'Open messages',
  },
  {
    title: 'Orders & payments',
    phase: 'Phase 2',
    body: 'Convert quotations to orders with agreed terms; verify invoices, COO, bills of lading.',
    to: null,
    cta: 'Coming soon',
  },
]

export default function DashboardPage() {
  const { user, updateProfile } = useAuth()
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [homeCountry, setHomeCountry] = useState(user?.homeCountry ?? '')
  const [defaultCurrency, setDefaultCurrency] = useState(user?.defaultCurrency ?? 'NGN')
  const [pending, setPending] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)
  const [editing, setEditing] = useState(false)

  if (!user) return null

  async function handleSave(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setSaved(false)
    setError(null)
    try {
      await updateProfile({
        displayName: displayName || undefined,
        homeCountry: homeCountry || undefined,
        defaultCurrency,
      })
      setSaved(true)
      setEditing(false)
    } catch (err) {
      setError(err as { title?: string; detail?: string })
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Welcome back, {user.displayName}</h1>
            <p className="mt-1 text-sm text-stone-500">
              Your cross-border trading workspace takes shape in Phase 1.
            </p>
          </div>
          <button
            onClick={() => setEditing((v) => !v)}
            className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50"
          >
            {editing ? 'Cancel' : 'Edit profile'}
          </button>
        </div>

        {editing ? (
          <form onSubmit={handleSave} className="mt-6 max-w-lg space-y-4 rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
            <h2 className="text-lg font-semibold">Profile</h2>
            <ErrorAlert title={error?.title} detail={error?.detail} />
            <Field
              label="Display name"
              id="me-displayName"
              required
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
            />
            <div className="grid gap-4 sm:grid-cols-2">
              <Field
                label="Home country (ISO-2)"
                id="me-country"
                maxLength={2}
                value={homeCountry}
                onChange={(e) => setHomeCountry(e.target.value.toUpperCase())}
              />
              <Field
                label="Default currency (ISO-3)"
                id="me-currency"
                maxLength={3}
                value={defaultCurrency}
                onChange={(e) => setDefaultCurrency(e.target.value.toUpperCase())}
              />
            </div>
            <div className="flex items-center gap-3">
              <SubmitButton type="submit" pending={pending} className="!w-auto px-6">
                Save changes
              </SubmitButton>
              {saved ? <span className="text-sm font-medium text-emerald-700">Saved</span> : null}
            </div>
          </form>
        ) : (
          <dl className="mt-6 grid gap-4 rounded-xl border border-stone-200 bg-white p-6 shadow-sm sm:grid-cols-4">
            {[
              ['Email', user.email],
              ['Roles', user.roles.join(', ')],
              ['Home country', user.homeCountry ?? '—'],
              ['Currency', user.defaultCurrency],
            ].map(([label, value]) => (
              <div key={label}>
                <dt className="text-xs font-semibold uppercase tracking-wide text-stone-500">{label}</dt>
                <dd className="mt-1 text-sm font-medium">{value}</dd>
              </div>
            ))}
          </dl>
        )}

        <section className="mt-10">
          <h2 className="text-lg font-semibold">Trading workspace</h2>
          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {quickActions.map((m) =>
              m.to ? (
                <Link
                  key={m.title}
                  to={m.to}
                  className="group rounded-xl border border-stone-200 bg-white p-5 shadow-sm transition hover:border-brand-300 hover:shadow"
                >
                  <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">{m.phase}</p>
                  <h3 className="mt-1.5 font-semibold group-hover:text-brand-700">{m.title}</h3>
                  <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{m.body}</p>
                  <p className="mt-3 text-sm font-semibold text-brand-700">{m.cta} →</p>
                </Link>
              ) : (
                <div key={m.title} className="rounded-xl border border-stone-200 bg-white p-5 shadow-sm">
                  <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">{m.phase}</p>
                  <h3 className="mt-1.5 font-semibold">{m.title}</h3>
                  <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{m.body}</p>
                  <p className="mt-3 text-sm font-semibold text-stone-400">{m.cta}</p>
                </div>
              ),
            )}
          </div>

          <div className="mt-6 rounded-xl border border-stone-200 bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold text-stone-700">Quick actions</h3>
            <div className="mt-3 flex flex-wrap gap-2">
              <Link to="/business" className="rounded-lg bg-brand-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-brand-700">
                Manage business
              </Link>
              {user.roles.some((r) => SELLER_ROLES.includes(r)) ? (
                <Link to="/products/new" className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50">
                  New listing
                </Link>
              ) : null}
              {user.roles.some((r) => BUYER_ROLES.includes(r)) ? (
                <Link to="/rfqs/new" className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50">
                  Publish RFQ
                </Link>
              ) : null}
              <Link to="/notifications" className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50">
                Notifications
              </Link>
            </div>
          </div>
        </section>
      </main>
    </div>
  )
}