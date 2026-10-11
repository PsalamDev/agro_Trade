import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import CountrySelect from '../components/CountrySelect'
import { ErrorAlert, Field, SelectField, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'
import { DOMESTIC_BUYER_ROLE } from '../types/auth'
import { GlobeIcon, LockIcon, MailIcon, MapPinIcon, UserIcon } from '../components/icons'

const CURRENCIES = ['NGN', 'KES', 'GHS', 'ZAR', 'EGP', 'USD', 'EUR', 'GBP', 'CNY', 'AED']

const INTENTS = [
  {
    id: 'domestic',
    icon: MapPinIcon,
    title: 'Buy locally',
    body: 'Shop produce with domestic delivery — check out straight from the cart, no business verification needed.',
  },
  {
    id: 'international',
    icon: GlobeIcon,
    title: 'Trade internationally',
    body: 'Import, distribute or bulk-source across borders. You set up your verified business profile after signup.',
  },
] as const

type Intent = (typeof INTENTS)[number]['id']

export default function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [intent, setIntent] = useState<Intent>('domestic')
  const [internationalRole, setInternationalRole] = useState('Importer')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [homeCountry, setHomeCountry] = useState('')
  const [defaultCurrency, setDefaultCurrency] = useState('NGN')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{
    title?: string
    detail?: string
    fieldErrors?: Record<string, string[]>
  } | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const roles = intent === 'domestic' ? [DOMESTIC_BUYER_ROLE] : [internationalRole]
      await register({
        email,
        password,
        displayName,
        homeCountry: homeCountry || undefined,
        defaultCurrency,
        roles,
      })
      // Each intent lands on its own onboarding: domestic buyers go straight to the
      // buy-locally marketplace, international traders to the dashboard (business
      // verification is prompted there).
      navigate(intent === 'domestic' ? '/marketplace?domesticOnly=1' : '/dashboard', { replace: true })
    } catch (err) {
      setError(err as { title?: string; detail?: string; fieldErrors?: Record<string, string[]> })
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="flex flex-1 items-center justify-center px-4 py-14">
        <div className="w-full max-w-lg">
          <div className="animate-fade-up rounded-3xl border border-stone-200 bg-white p-7 shadow-pop sm:p-8">
            <span className="grid h-12 w-12 place-items-center rounded-2xl bg-brand-50 text-brand-700">
              <UserIcon className="h-5 w-5" />
            </span>
            <h1 className="font-display mt-5 text-2xl font-bold tracking-tight">
              Create your trading account
            </h1>
            <p className="mt-1.5 text-sm text-stone-500">
              Choose how you’ll trade — the account, roles and next steps adapt to your path.
            </p>

            <div className="mt-6 grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="How will you trade?">
              {INTENTS.map(({ id, icon: Icon, title, body }) => {
                const selected = intent === id
                return (
                  <button
                    key={id}
                    type="button"
                    role="radio"
                    aria-checked={selected}
                    onClick={() => setIntent(id)}
                    className={`flex h-full flex-col rounded-2xl border p-4 text-left transition duration-200 ease-soft ${
                      selected
                        ? 'border-brand-500 bg-brand-50/60 shadow-soft ring-4 ring-brand-500/10'
                        : 'border-stone-200 bg-white hover:border-brand-200 hover:bg-stone-50'
                    }`}
                  >
                    <span
                      className={`grid h-9 w-9 place-items-center rounded-xl ${
                        selected ? 'bg-brand-600 text-white' : 'bg-stone-100 text-stone-500'
                      }`}
                    >
                      <Icon className="h-4.5 w-4.5" />
                    </span>
                    <span className="font-display mt-3 text-sm font-semibold text-stone-900">{title}</span>
                    <span className="mt-1 text-xs leading-relaxed text-stone-500">{body}</span>
                  </button>
                )
              })}
            </div>

            <form onSubmit={handleSubmit} className="mt-7 space-y-4">
              <ErrorAlert title={error?.title} detail={error?.detail} fieldErrors={error?.fieldErrors} />

              <Field
                label="Business / contact name"
                id="displayName"
                autoComplete="name"
                required
                icon={<UserIcon className="h-4 w-4" />}
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
              />
              <Field
                label="Email"
                id="email"
                type="email"
                autoComplete="email"
                required
                icon={<MailIcon className="h-4 w-4" />}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              <Field
                label="Password (min 8 chars, upper + lower + digit)"
                id="password"
                type="password"
                autoComplete="new-password"
                required
                minLength={8}
                icon={<LockIcon className="h-4 w-4" />}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />

              {intent === 'international' ? (
                <SelectField
                  label="Your role"
                  id="register-role"
                  options={['Importer', 'Distributor']}
                  values={['Importer', 'Distributor']}
                  value={internationalRole}
                  onChange={(e) => setInternationalRole(e.target.value)}
                />
              ) : null}

              <div className="grid gap-4 sm:grid-cols-2">
                <CountrySelect
                  label="Home country"
                  id="homeCountry"
                  value={homeCountry}
                  onChange={setHomeCountry}
                  hint="Optional — search by country name, the ISO-2 code is stored automatically."
                />
                <SelectField
                  label="Default currency"
                  id="defaultCurrency"
                  options={CURRENCIES}
                  values={CURRENCIES}
                  value={defaultCurrency}
                  onChange={(e) => setDefaultCurrency(e.target.value)}
                />
              </div>

              <SubmitButton type="submit" pending={pending}>
                {intent === 'domestic' ? 'Create account & start shopping' : 'Create trading account'}
              </SubmitButton>
            </form>

            <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
              Already registered?{' '}
              <Link to="/login" className="font-semibold text-brand-700 hover:underline">
                Sign in
              </Link>
            </p>
          </div>
        </div>
      </main>
    </div>
  )
}