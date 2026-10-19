import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, PasswordRules, SubmitButton } from '../components/ui'
import { CheckCircleIcon, EyeIcon, EyeOffIcon, LockIcon, MailIcon } from '../components/icons'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const [email, setEmail] = useState(params.get('email') ?? '')
  const [code, setCode] = useState((params.get('code') ?? '').replace(/\D/g, '').slice(0, 6))
  const [newPassword, setNewPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [pending, setPending] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<{
    title?: string
    detail?: string
    fieldErrors?: Record<string, string[]>
  } | null>(null)

  const emailFromLink = Boolean(params.get('email'))
  // The emailed one-click link carries the code, so the form arrives ready to submit.
  const codeFromLink = code.length === 6

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await api.post('/auth/reset-password', { email, code, newPassword })
      setDone(true)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <AuthLayout minimal>
      <h1 className="font-display text-2xl font-bold tracking-tight">Choose a new password</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        {codeFromLink ? (
          <>
            We’ve filled in the code from your email. Choose a new password to finish.
          </>
        ) : emailFromLink ? (
          <>
            Enter the 6-digit code we sent to{' '}
            <span className="font-medium text-stone-700">{email}</span>, then pick a new password.
          </>
        ) : (
          'Enter the 6-digit code from your email, then pick a new password.'
        )}
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
          <ErrorAlert title={error?.title} detail={error?.detail} fieldErrors={error?.fieldErrors} />
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
            label="Reset code"
            id="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="[0-9]{6}"
            maxLength={6}
            required
            placeholder="000000"
            style={{ letterSpacing: '0.4em' }}
            hint="The 6-digit code from your email."
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
          />
          <div>
            <Field
              label="New password"
              id="newPassword"
              type={showPassword ? 'text' : 'password'}
              autoComplete="new-password"
              required
              minLength={8}
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
            <PasswordRules value={newPassword} />
          </div>
          <SubmitButton type="submit" pending={pending}>
            Set new password
          </SubmitButton>
        </form>
      )}

      <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
        Didn’t get a code?{' '}
        <Link to="/forgot-password" className="font-semibold text-brand-700 hover:underline">
          Send a new one
        </Link>
      </p>
    </AuthLayout>
  )
}
