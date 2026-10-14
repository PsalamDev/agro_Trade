import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { CheckCircleIcon, MailIcon } from '../components/icons'

export default function ConfirmEmailPage() {
  const [params] = useSearchParams()
  const [email, setEmail] = useState(params.get('email') ?? '')
  const [token, setToken] = useState(params.get('token') ?? '')
  const [pending, setPending] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/confirm-email', { email, token })
      setDone(true)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <AuthLayout
      eyebrow="Email confirmation"
      headline="Confirm your email."
      blurb="Finish setting up your account by confirming your email address."
    >
      <h1 className="font-display text-2xl font-bold tracking-tight">Confirm your email</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        Paste the token from your confirmation email to activate your account.
      </p>

      {done ? (
        <div className="mt-7 flex gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
          <CheckCircleIcon className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div>
            <p className="font-semibold">Email confirmed — welcome aboard.</p>
            <Link
              to="/login"
              className="mt-3 inline-block font-semibold text-emerald-700 hover:underline"
            >
              Sign in
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
          <Field
            label="Confirmation token"
            id="token"
            required
            value={token}
            onChange={(e) => setToken(e.target.value)}
          />
          <SubmitButton type="submit" pending={pending}>
            Confirm
          </SubmitButton>
        </form>
      )}
    </AuthLayout>
  )
}
