import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { CheckCircleIcon } from '../components/icons'

export default function VerifyCodePage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const email = params.get('email') ?? ''

  const [code, setCode] = useState((params.get('code') ?? '').replace(/\D/g, '').slice(0, 6))
  const [pending, setPending] = useState(false)
  const [resent, setResent] = useState(false)
  const [error, setError] = useState<{
    title?: string
    detail?: string
    fieldErrors?: Record<string, string[]>
  } | null>(null)

  // The step only makes sense with an address to verify; otherwise restart the flow.
  if (!email) {
    return <Navigate to="/forgot-password" replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      // Ask the server to confirm the code before advancing to the new-password step.
      await api.post('/auth/verify-reset-code', { email, code })
      navigate(`/reset-password?email=${encodeURIComponent(email)}&code=${encodeURIComponent(code)}`)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  async function resend() {
    setError(null)
    setResent(false)
    try {
      await api.post('/auth/forgot-password', { email })
      setResent(true)
    } catch (err) {
      setError(problemFrom(err))
    }
  }

  return (
    <AuthLayout minimal>
      <h1 className="font-display text-2xl font-bold tracking-tight">Enter your verification code</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        We’ve sent a 6-digit code to{' '}
        <span className="font-medium text-stone-700">{email}</span>. Enter it below to verify it’s
        you. It expires in 10 minutes.
      </p>

      <form onSubmit={handleSubmit} className="mt-7 space-y-4">
        <ErrorAlert title={error?.title} detail={error?.detail} fieldErrors={error?.fieldErrors} />
        <Field
          label="Verification code"
          id="code"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]{6}"
          maxLength={6}
          required
          placeholder="000000"
          style={{ letterSpacing: '0.4em' }}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
        />
        <SubmitButton type="submit" pending={pending}>
          Verify code
        </SubmitButton>
      </form>

      {resent ? (
        <div className="mt-4 flex gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
          <CheckCircleIcon className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <p>We’ve sent a new code. The previous code no longer works.</p>
        </div>
      ) : null}

      <div className="mt-5 flex items-center justify-between gap-3 text-sm">
        <button
          type="button"
          onClick={() => void resend()}
          className="font-medium text-stone-500 transition duration-200 ease-soft hover:text-stone-800"
        >
          Resend code
        </button>
        <Link to="/forgot-password" className="font-semibold text-brand-700 hover:underline">
          Use a different email
        </Link>
      </div>
    </AuthLayout>
  )
}
