import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'

const COUNTRIES = ['NG', 'KE', 'GH', 'ZA', 'EG', 'ET', 'SN', 'TZ', 'UG', 'RW', 'CI', 'CM', 'MA', 'OTHER']

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
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-lg px-4 py-12">
        <h1 className="text-2xl font-bold tracking-tight">Create your trading account</h1>
        <p className="mt-1 text-sm text-stone-500">
          Join as a buyer / importer — publish RFQs and source verified imports. Supplier listings are managed by
          the platform.
        </p>

        <form onSubmit={handleSubmit} className="mt-8 space-y-4">
          <ErrorAlert title={error?.title} detail={error?.detail} fieldErrors={error?.fieldErrors} />

          <Field
            label="Business / contact name"
            id="displayName"
            autoComplete="name"
            required
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />
          <Field
            label="Email"
            id="email"
            type="email"
            autoComplete="email"
            required
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
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-stone-700">Home country</span>
              <select
                value={homeCountry}
                onChange={(e) => setHomeCountry(e.target.value)}
                className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
              >
                <option value="">Select…</option>
                {COUNTRIES.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-stone-700">Default currency</span>
              <select
                value={defaultCurrency}
                onChange={(e) => setDefaultCurrency(e.target.value)}
                className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
              >
                {['NGN', 'KES', 'GHS', 'ZAR', 'EGP', 'USD', 'EUR', 'GBP', 'CNY', 'AED'].map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <SubmitButton type="submit" pending={pending}>
            Create account
          </SubmitButton>
        </form>

        <p className="mt-6 text-center text-sm text-stone-500">
          Already registered?{' '}
          <Link to="/login" className="font-medium text-brand-700 hover:underline">
            Sign in
          </Link>
        </p>
      </main>
    </div>
  )
}