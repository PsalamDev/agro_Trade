import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { ErrorAlert, Field, SelectField, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'
import { LockIcon, MailIcon, UserIcon } from '../components/icons'

const COUNTRIES = ['NG', 'KE', 'GH', 'ZA', 'EG', 'ET', 'SN', 'TZ', 'UG', 'RW', 'CI', 'CM', 'MA', 'OTHER']
const CURRENCIES = ['NGN', 'KES', 'GHS', 'ZAR', 'EGP', 'USD', 'EUR', 'GBP', 'CNY', 'AED']

export default function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()

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
      await register({
        email,
        password,
        displayName,
        homeCountry: homeCountry || undefined,
        defaultCurrency,
      })
      navigate('/dashboard', { replace: true })
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
              Join as a buyer / importer — publish RFQs and source verified imports. Supplier
              listings are managed by the platform.
            </p>

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

              <div className="grid gap-4 sm:grid-cols-2">
                <SelectField
                  label="Home country"
                  id="homeCountry"
                  options={COUNTRIES}
                  values={COUNTRIES}
                  value={homeCountry}
                  onChange={(e) => setHomeCountry(e.target.value)}
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
                Create account
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