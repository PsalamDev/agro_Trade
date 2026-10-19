import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import CountrySelect from '../components/CountrySelect'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, PasswordRules, SelectField, SubmitButton } from '../components/ui'
import { DOMESTIC_BUYER_ROLE } from '../types/auth'
import {
  EyeIcon,
  EyeOffIcon,
  GlobeIcon,
  LockIcon,
  MailIcon,
  MapPinIcon,
  UserIcon,
} from '../components/icons'

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
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [displayName, setDisplayName] = useState('')
  const [homeCountry, setHomeCountry] = useState('')
  const [defaultCurrency, setDefaultCurrency] = useState('NGN')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{
    title?: string
    detail?: string
    fieldErrors?: Record<string, string[]>
  } | null>(null)

  const activeIntent = INTENTS.find((option) => option.id === intent) ?? INTENTS[0]

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const roles = [DOMESTIC_BUYER_ROLE]
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
    <AuthLayout
      eyebrow="Create your account"
      headline="Start trading in minutes."
      blurb="One account for local purchasing and international trade — add your verified business profile whenever you're ready."
    >
      <h1 className="font-display text-2xl font-bold tracking-tight">Create your trading account</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        Choose how you’ll trade — the account, roles and next steps adapt to your path.
      </p>

      <div
        role="radiogroup"
        aria-label="How will you trade?"
        className="mt-6 grid grid-cols-2 gap-1 rounded-2xl border border-stone-200 bg-stone-100/70 p-1"
      >
        {INTENTS.map(({ id, icon: Icon, title }) => {
          const selected = intent === id
          return (
            <button
              key={id}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => setIntent(id)}
              className={`inline-flex items-center justify-center gap-2 rounded-xl px-3 py-2.5 text-sm font-semibold transition duration-200 ease-soft ${
                selected
                  ? 'bg-white text-brand-700 shadow-soft ring-1 ring-stone-200'
                  : 'text-stone-600 hover:text-stone-900'
              }`}
            >
              <Icon className="h-4 w-4" />
              {title}
            </button>
          )
        })}
      </div>
      <p className="mt-2.5 text-xs leading-relaxed text-stone-500">{activeIntent.body}</p>

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
        <div>
          <Field
            label="Password"
            id="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="new-password"
            required
            minLength={8}
            icon={<LockIcon className="h-4 w-4" />}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            trailing={
              <button
                type="button"
                onClick={() => setShowPassword((visible) => !visible)}
                onMouseDown={(e) => e.preventDefault()}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                aria-pressed={showPassword}
                className="grid h-8 w-8 place-items-center rounded-lg text-stone-400 transition duration-200 ease-soft hover:bg-stone-100 hover:text-stone-700"
              >
                {showPassword ? <EyeOffIcon className="h-4 w-4" /> : <EyeIcon className="h-4 w-4" />}
              </button>
            }
          />
          <PasswordRules value={password} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <CountrySelect
            label="Home country"
            id="homeCountry"
            value={homeCountry}
            onChange={setHomeCountry}
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

        <p className="text-center text-xs leading-relaxed text-stone-400">
          By creating an account you agree to trade under Achievers Global Consult’s terms. We use
          your details only to operate your trade account.
        </p>
      </form>

      <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
        Already registered?{' '}
        <Link to="/login" className="font-semibold text-brand-700 hover:underline">
          Sign in
        </Link>
      </p>
    </AuthLayout>
  )
}
