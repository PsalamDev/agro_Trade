import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { CheckCircleIcon, MailIcon } from '../components/icons'

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
    <AuthLayout
      eyebrow="Account recovery"
      headline="Back to your trade desk."
      blurb="Enter the email on your account and we'll send step-by-step reset instructions."
    >
      <h1 className="font-display text-2xl font-bold tracking-tight">Reset your password</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        We’ll email a secure link so you can choose a new password.
      </p>

      {sent ? (
        <div className="mt-7 flex gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
          <CheckCircleIcon className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div>
            <p className="font-semibold">Check your inbox.</p>
            <p className="mt-0.5 text-emerald-800">
              If an account exists for that email, we&apos;ve sent reset instructions (in development
              the token is written to the API log).
            </p>
            <Link
              to="/login"
              className="mt-3 inline-block font-semibold text-emerald-700 hover:underline"
            >
              Back to sign in
            </Link>
          </div>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="mt-7 space-y-4">
          <ErrorAlert title={error?.title} detail={error?.detail} />
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
          <SubmitButton type="submit" pending={pending}>
            Send reset link
          </SubmitButton>
        </form>
      )}

      <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
        Remembered it?{' '}
        <Link to="/login" className="font-semibold text-brand-700 hover:underline">
          Sign in
        </Link>
      </p>
    </AuthLayout>
  )
}
