import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { CheckCircleIcon, EyeIcon, EyeOffIcon, LockIcon, MailIcon } from '../components/icons'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const [email, setEmail] = useState(params.get('email') ?? '')
  const [token, setToken] = useState(params.get('token') ?? '')
  const [newPassword, setNewPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [pending, setPending] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/reset-password', { email, token, newPassword })
      setDone(true)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <AuthLayout
      eyebrow="Account recovery"
      headline="Set a new password."
      blurb="Choose a strong password to secure your trade account, then sign back in."
    >
      <h1 className="font-display text-2xl font-bold tracking-tight">Choose a new password</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        Paste the token from your reset email and pick a new password.
      </p>

      {done ? (
        <div className="mt-7 flex gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
          <CheckCircleIcon className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div>
            <p className="font-semibold">Password updated.</p>
            <Link
              to="/login"
              className="mt-3 inline-block font-semibold text-emerald-700 hover:underline"
            >
              Sign in with your new password
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
            label="Reset token"
            id="token"
            required
            value={token}
            onChange={(e) => setToken(e.target.value)}
          />
          <Field
            label="New password"
            id="newPassword"
            type={showPassword ? 'text' : 'password'}
            autoComplete="new-password"
            required
            minLength={8}
            hint="At least 8 characters, with an uppercase letter, a lowercase letter and a number."
            icon={<LockIcon className="h-4 w-4" />}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
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
          <SubmitButton type="submit" pending={pending}>
            Set new password
          </SubmitButton>
        </form>
      )}
    </AuthLayout>
  )
}
