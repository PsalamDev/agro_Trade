import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { Button, ErrorAlert, Field, SubmitButton } from '../components/ui'
import { CheckCircleIcon, MailIcon } from '../components/icons'

export default function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [pending, setPending] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  async function sendCode(target: string) {
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/forgot-password', { email: target })
      setSent(true)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    void sendCode(email)
  }

  return (
    <AuthLayout minimal>
      <h1 className="font-display text-2xl font-bold tracking-tight">
        {sent ? 'Check your email' : 'Forgot your password?'}
      </h1>
      <p className="mt-1.5 text-sm text-stone-500">
        {sent
          ? `We’ve sent a 6-digit reset code to ${email}.`
          : 'Enter the email on your account and we’ll send you a 6-digit code to reset it.'}
      </p>

      {sent ? (
        <div className="mt-7 space-y-4">
          <ErrorAlert title={error?.title} detail={error?.detail} />
          <div className="flex gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
            <CheckCircleIcon className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
            <div>
              <p className="font-semibold">Check your inbox.</p>
              <p className="mt-0.5 text-emerald-800">
                Open the link in the email, or copy the 6-digit code and enter it on the next
                screen. It expires in 10 minutes.
              </p>
            </div>
          </div>

          <Button
            size="lg"
            className="w-full"
            onClick={() => navigate(`/reset-password?email=${encodeURIComponent(email)}`)}
          >
            Enter the code
          </Button>

          <button
            type="button"
            onClick={() => void sendCode(email)}
            disabled={pending}
            className="w-full rounded-xl py-2 text-sm font-medium text-stone-500 transition duration-200 ease-soft hover:text-stone-800 disabled:opacity-60"
          >
            {pending ? 'Sending…' : 'Resend code'}
          </button>
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
            Send reset code
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
