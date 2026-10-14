import { useState } from 'react'
import type { ComponentType, FormEvent, SVGProps } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import CountrySelect from '../components/CountrySelect'
import { Button, Card, ErrorAlert, Field, PageHeader, SelectField, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'
import { BUYER_ROLES, COMPANY_ROLE, DOMESTIC_BUYER_ROLE } from '../types/auth'
import {
  BuildingIcon,
  CheckCircleIcon,
  CoinsIcon,
  CompassIcon,
  FileTextIcon,
  GlobeIcon,
  GridIcon,
  MapPinIcon,
  MessageIcon,
  PackageIcon,
  PencilIcon,
  SearchIcon,
  TagIcon,
  TruckIcon,
  UserIcon,
} from '../components/icons'

const CURRENCIES = ['NGN', 'KES', 'GHS', 'ZAR', 'EGP', 'USD', 'EUR', 'GBP', 'CNY', 'AED']

const LSP_ROLE = 'LogisticsProvider'

/** Friendly names for the role identifiers the API returns. */
const ROLE_LABELS: Record<string, string> = {
  Buyer: 'Buyer',
  Importer: 'Importer',
  Distributor: 'Distributor',
  Company: 'Supplier',
  LogisticsProvider: 'Logistics provider',
  Administrator: 'Administrator',
}

interface WorkspaceAction {
  title: string
  icon: ComponentType<SVGProps<SVGSVGElement>>
  body: string
  to: string
  cta: string
  buyersOnly?: boolean
  companyOnly?: boolean
  lspOnly?: boolean
}

/**
 * The tools a signed-in user can reach, gated by role.
 *
 * <p>
 * Discovery only — no roadmap or build-phase language reaches the screen. A destination a user's
 * roles cannot use is filtered out rather than shown greyed out, so the grid reads as "what you can
 * do" rather than "what you are missing".
 * </p>
 */
const WORKSPACE: WorkspaceAction[] = [
  {
    title: 'Marketplace',
    icon: GridIcon,
    body: 'Browse verified export listings and compare price, origin and trade terms.',
    to: '/marketplace',
    cta: 'Explore supply',
  },
  {
    title: 'Buy locally',
    icon: MapPinIcon,
    body: 'Produce that ships domestically with a flat delivery fee and a local address.',
    to: '/marketplace?domesticOnly=1',
    cta: 'Start shopping',
    buyersOnly: true,
  },
  {
    title: 'Find RFQs',
    icon: SearchIcon,
    body: 'Open sourcing requests from importers — respond with your full trade terms.',
    to: '/rfqs',
    cta: 'Find requests',
  },
  {
    title: 'My RFQs',
    icon: FileTextIcon,
    body: 'Requests you have published and the quotations they received.',
    to: '/my/rfqs',
    cta: 'Review RFQs',
    buyersOnly: true,
  },
  {
    title: 'Orders',
    icon: PackageIcon,
    body: 'Track orders, payments, documents and shipments end to end.',
    to: '/dashboard/orders',
    cta: 'Open orders',
  },
  {
    title: 'Messages',
    icon: MessageIcon,
    body: 'Negotiation threads, quotation replies and platform notices.',
    to: '/messages',
    cta: 'Open messages',
  },
  {
    title: 'My listings',
    icon: TagIcon,
    body: 'Publish and manage the supply your business offers.',
    to: '/my/products',
    cta: 'Manage listings',
    companyOnly: true,
  },
  {
    title: 'Business profile',
    icon: BuildingIcon,
    body: 'Company details, verification status and your public profile.',
    to: '/business',
    cta: 'Open business',
  },
  {
    title: 'Logistics',
    icon: TruckIcon,
    body: 'Book freight and follow consignments on the agreed Incoterm.',
    to: '/logistics/bookings',
    cta: 'View bookings',
  },
  {
    title: 'LSP profile',
    icon: CompassIcon,
    body: 'Your logistics provider listing and the routes you cover.',
    to: '/logistics/profile',
    cta: 'Open profile',
    lspOnly: true,
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

  const roles = user.roles
  const isBuyer = roles.some((r) => BUYER_ROLES.includes(r))
  const isCompany = roles.includes(COMPANY_ROLE)
  const isLsp = roles.includes(LSP_ROLE)
  const isDomesticOnly = roles.length === 1 && roles[0] === DOMESTIC_BUYER_ROLE

  const workspace = WORKSPACE.filter((action) => {
    if (action.buyersOnly && !isBuyer) return false
    if (action.companyOnly && !isCompany) return false
    if (action.lspOnly && !isLsp) return false
    return true
  })

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
    {
      label: 'Roles',
      value: user.roles.map((role) => ROLE_LABELS[role] ?? role).join(', '),
      icon: <CheckCircleIcon className="h-4 w-4" />,
    },
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
          subtitle={
            isDomesticOnly
              ? 'Your domestic shopping workspace — nearby produce with local delivery.'
              : 'Your cross-border trading workspace — sourcing, quotations, orders and logistics in one place.'
          }
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

        {user.buyerId ? <BuyerIdCard buyerId={user.buyerId} /> : null}

        <section className="mt-10">
          <h2 className="font-display text-lg font-semibold">Your account</h2>
          <p className="mt-1 text-sm text-stone-500">
            Who you trade as, and the defaults applied to new orders.
          </p>

          <div className="mt-4">
            {editing ? (
              <form onSubmit={handleSave} className="max-w-lg">
                <Card>
                  <div className="space-y-4">
                    <ErrorAlert title={error?.title} detail={error?.detail} />
                    <Field
                      label="Display name"
                      id="me-displayName"
                      required
                      value={displayName}
                      onChange={(e) => setDisplayName(e.target.value)}
                    />
                    <div className="grid gap-4 sm:grid-cols-2">
                      <CountrySelect
                        label="Home country"
                        id="me-country"
                        value={homeCountry}
                        onChange={setHomeCountry}
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
        </section>

        <section className="mt-12">
          <h2 className="font-display text-lg font-semibold">Workspace</h2>
          <p className="mt-1 text-sm text-stone-500">The tools available to your roles right now.</p>

          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {workspace.map(({ title, icon: Icon, body, to, cta }) => (
              <Link
                key={title}
                to={to}
                className="group flex flex-col rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
              >
                <span className="grid h-10 w-10 place-items-center rounded-xl bg-brand-50 text-brand-700 transition duration-300 ease-soft group-hover:bg-brand-100">
                  <Icon className="h-4.5 w-4.5" />
                </span>
                <h3 className="font-display mt-4 font-semibold transition group-hover:text-brand-700">
                  {title}
                </h3>
                <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{body}</p>
                <p className="mt-auto pt-4 text-sm font-semibold text-brand-700">{cta} →</p>
              </Link>
            ))}
          </div>
        </section>
      </main>
    </div>
  )
}

/**
 * The buyer's account number, on its own card rather than as one fact among several.
 *
 * <p>
 * It is the identifier a buyer quotes to the supplier and reads out to support, so it is the one
 * value on this page worth being able to find and copy at a glance. Clipboard access can be refused
 * — permissions, or a non-secure context — and the number is on screen either way, so a failed copy
 * has nothing to recover from and is left silent rather than raised as an error.
 * </p>
 */
function BuyerIdCard({ buyerId }: { buyerId: string }) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(buyerId)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      // The number is visible and selectable, so it can still be copied by hand.
    }
  }

  return (
    <Card className="mt-6 border-brand-200 bg-brand-50/40">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-brand-100 text-brand-700">
            <UserIcon className="h-5 w-5" />
          </span>
          <div className="min-w-0">
            <p className="text-xs font-medium tracking-wide text-brand-700 uppercase">Buyer ID</p>
            <p className="font-display truncate text-lg font-semibold tracking-tight text-stone-900">
              {buyerId}
            </p>
          </div>
        </div>
        <button
          type="button"
          onClick={() => void copy()}
          className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-brand-200 bg-white px-3 text-sm font-semibold text-brand-700 shadow-soft transition hover:bg-brand-50"
        >
          {copied ? <CheckCircleIcon className="h-4 w-4" /> : null}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <p className="mt-3 text-xs text-stone-600">
        Your permanent account number. Quote it when you contact support; it is what the supplier
        references against your orders, and it never changes.
      </p>
    </Card>
  )
}
