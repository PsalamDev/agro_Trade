import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [pending, setPending] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/forgot-password', { email })
      setSent(true)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto flex max-w-md flex-col px-4 py-16">
        <h1 className="text-2xl font-bold tracking-tight">Reset your password</h1>
        {sent ? (
          <div className="mt-8 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
            <p className="font-medium">Check your inbox.</p>
            <p className="mt-1">
              If an account exists for that email, we&apos;ve sent reset instructions (in development the
              token is written to the API log).
            </p>
            <Link to="/login" className="mt-3 inline-block font-semibold text-emerald-700 hover:underline">
              Back to sign in
            </Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="mt-8 space-y-4">
            <ErrorAlert title={error?.title} detail={error?.detail} />
            <Field
              label="Email"
              id="email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
            <SubmitButton type="submit" pending={pending}>
              Send reset link
            </SubmitButton>
          </form>
        )}
        <p className="mt-6 text-sm text-stone-500">
          Remembered it?{' '}
          <Link to="/login" className="font-medium text-brand-700 hover:underline">
            Sign in
          </Link>
        </p>
      </main>
    </div>
  )
}