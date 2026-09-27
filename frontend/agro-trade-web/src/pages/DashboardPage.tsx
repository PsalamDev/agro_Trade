import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { Button, Card, ErrorAlert, Field, PageHeader, SelectField, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'
import { COMPANY_ROLE } from '../types/auth'
import {
  BellIcon,
  BuildingIcon,
  CheckCircleIcon,
  CoinsIcon,
  FileTextIcon,
  GlobeIcon,
  GridIcon,
  MessageIcon,
  PackageIcon,
  PencilIcon,
  SearchIcon,
} from '../components/icons'

const BUYER_ROLES = ['Buyer', 'Importer', 'Distributor']
const COUNTRIES = ['NG', 'KE', 'GH', 'ZA', 'EG', 'ET', 'SN', 'TZ', 'UG', 'RW', 'CI', 'CM', 'MA', 'OTHER']
const CURRENCIES = ['NGN', 'KES', 'GHS', 'ZAR', 'EGP', 'USD', 'EUR', 'GBP', 'CNY', 'AED']

const quickActions = [
  {
    title: 'Marketplace',
    phase: 'Live · Phase 1',
    icon: GridIcon,
    body: 'Browse verified export listings, filter by origin, destination, and price.',
    to: '/marketplace',
    cta: 'Explore supply',
  },
  {
    title: 'RFQ directory',
    phase: 'Live · Phase 1',
    icon: SearchIcon,
    body: 'Open importers’ sourcing requests. Verified suppliers quote with full trade terms.',
    to: '/rfqs',
    cta: 'Find requests',
  },
  {
    title: 'Messages & RFQs',
    phase: 'Live · Phase 1',
    icon: MessageIcon,
    body: 'Negotiation threads per RFQ, plus RFQ/quotation/verification notifications.',
    to: '/messages',
    cta: 'Open messages',
  },
  {
    title: 'Orders & payments',
    phase: 'Phase 2',
    icon: CoinsIcon,
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

  const profileFacts = [
    { label: 'Email', value: user.email, icon: <MessageIcon className="h-4 w-4" /> },
    { label: 'Roles', value: user.roles.join(', '), icon: <CheckCircleIcon className="h-4 w-4" /> },
    { label: 'Home country', value: user.homeCountry ?? '—', icon: <GlobeIcon className="h-4 w-4" /> },
    { label: 'Currency', value: user.defaultCurrency, icon: <CoinsIcon className="h-4 w-4" /> },
  ]

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Dashboard"
          title={`Welcome back, ${user.displayName}`}
          subtitle="Your cross-border trading workspace takes shape in Phase 1."
          action={
            <Button
              variant={editing ? 'ghost' : 'secondary'}
              onClick={() => {
                setEditing((v) => !v)
                setSaved(false)
              }}
            >
              <PencilIcon className="h-4 w-4" />
              {editing ? 'Cancel' : 'Edit profile'}
            </Button>
          }
        />

        <div className="mt-6">
          {editing ? (
            <form onSubmit={handleSave} className="max-w-lg space-y-4">
              <Card>
                <h2 className="font-display text-lg font-semibold">Profile</h2>
                <div className="mt-5 space-y-4">
                  <ErrorAlert title={error?.title} detail={error?.detail} />
                  <Field
                    label="Display name"
                    id="me-displayName"
                    required
                    value={displayName}
                    onChange={(e) => setDisplayName(e.target.value)}
                  />
                  <div className="grid gap-4 sm:grid-cols-2">
                    <SelectField
                      label="Home country"
                      id="me-country"
                      options={COUNTRIES}
                      values={COUNTRIES}
                      value={homeCountry}
                      onChange={(e) => setHomeCountry(e.target.value)}
                    />
                    <SelectField
                      label="Default currency"
                      id="me-currency"
                      options={CURRENCIES}
                      values={CURRENCIES}
                      value={defaultCurrency}
                      onChange={(e) => setDefaultCurrency(e.target.value)}
                    />
                  </div>
                  <div className="flex items-center gap-3">
                    <SubmitButton type="submit" pending={pending} className="!w-auto px-6">
                      Save changes
                    </SubmitButton>
                    {saved ? (
                      <span className="flex items-center gap-1.5 text-sm font-medium text-emerald-700">
                        <CheckCircleIcon className="h-4 w-4" />
                        Saved
                      </span>
                    ) : null}
                  </div>
                </div>
              </Card>
            </form>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {profileFacts.map((fact) => (
                <div
                  key={fact.label}
                  className="flex items-center gap-3 rounded-2xl border border-stone-200 bg-white px-4 py-3.5 shadow-soft"
                >
                  <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700">
                    {fact.icon}
                  </span>
                  <div className="min-w-0">
                    <p className="truncate text-xs font-medium tracking-wide text-stone-500 uppercase">
                      {fact.label}
                    </p>
                    <p className="truncate font-display text-sm font-semibold text-stone-900">
                      {fact.value}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        <section className="mt-12">
          <h2 className="font-display text-lg font-semibold">Trading workspace</h2>
          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {quickActions.map(({ title, phase, icon: Icon, body, to, cta }) =>
              to ? (
                <Link
                  key={title}
                  to={to}
                  className="group flex flex-col rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
                >
                  <span className="grid h-10 w-10 place-items-center rounded-xl bg-brand-50 text-brand-700 transition duration-300 ease-soft group-hover:bg-brand-100">
                    <Icon className="h-4.5 w-4.5" />
                  </span>
                  <p className="mt-4 text-[11px] font-semibold tracking-wide text-brand-700 uppercase">
                    {phase}
                  </p>
                  <h3 className="font-display mt-1.5 font-semibold transition group-hover:text-brand-700">
                    {title}
                  </h3>
                  <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{body}</p>
                  <p className="mt-auto pt-4 text-sm font-semibold text-brand-700">{cta} →</p>
                </Link>
              ) : (
                <div
                  key={title}
                  className="flex flex-col rounded-2xl border border-dashed border-stone-300 bg-stone-50/60 p-5"
                >
                  <span className="grid h-10 w-10 place-items-center rounded-xl bg-stone-200/70 text-stone-500">
                    <Icon className="h-4.5 w-4.5" />
                  </span>
                  <p className="mt-4 text-[11px] font-semibold tracking-wide text-stone-500 uppercase">
                    {phase}
                  </p>
                  <h3 className="font-display mt-1.5 font-semibold text-stone-700">{title}</h3>
                  <p className="mt-1.5 text-sm leading-relaxed text-stone-500">{body}</p>
                  <p className="mt-auto pt-4 text-sm font-semibold text-stone-400">{cta}</p>
                </div>
              ),
            )}
          </div>

          <Card className="mt-6">
            <h3 className="font-display text-sm font-semibold text-stone-800">Quick actions</h3>
            <div className="mt-4 flex flex-wrap gap-2">
              <Link
                to="/business"
                className="inline-flex h-10 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700"
              >
                <BuildingIcon className="h-4 w-4" />
                Manage business
              </Link>
              {user.roles.includes(COMPANY_ROLE) ? (
                <Link
                  to="/products/new"
                  className="inline-flex h-10 items-center gap-2 rounded-xl border border-stone-300 bg-white px-4 text-sm font-semibold text-stone-700 shadow-soft transition duration-200 ease-soft hover:bg-stone-50"
                >
                  <PackageIcon className="h-4 w-4" />
                  New listing
                </Link>
              ) : null}
              {user.roles.some((r) => BUYER_ROLES.includes(r)) ? (
                <Link
                  to="/rfqs/new"
                  className="inline-flex h-10 items-center gap-2 rounded-xl border border-stone-300 bg-white px-4 text-sm font-semibold text-stone-700 shadow-soft transition duration-200 ease-soft hover:bg-stone-50"
                >
                  <FileTextIcon className="h-4 w-4" />
                  Publish RFQ
                </Link>
              ) : null}
              <Link
                to="/notifications"
                className="inline-flex h-10 items-center gap-2 rounded-xl border border-stone-300 bg-white px-4 text-sm font-semibold text-stone-700 shadow-soft transition duration-200 ease-soft hover:bg-stone-50"
              >
                <BellIcon className="h-4 w-4" />
                Notifications
              </Link>
            </div>
          </Card>
        </section>
      </main>
    </div>
  )
}
