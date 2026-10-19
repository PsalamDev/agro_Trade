import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { MailIcon } from '../components/icons'

export default function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/forgot-password', { email })
      // Straight to the code entry: the email carries a six-digit code, not a link.
      navigate(`/reset-password?email=${encodeURIComponent(email)}`)
    } catch (err) {
      setError(problemFrom(err))
      setPending(false)
    }
  }

  return (
    <AuthLayout minimal>
      <h1 className="font-display text-2xl font-bold tracking-tight">Forgot your password?</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        Enter the email on your account and we’ll send you a 6-digit code to reset it.
      </p>

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

      <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
        Remembered it?{' '}
        <Link to="/login" className="font-semibold text-brand-700 hover:underline">
          Sign in
        </Link>
      </p>
    </AuthLayout>
  )
}
