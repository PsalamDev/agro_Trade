import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { AuthLayout } from '../components/AuthLayout'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AlertCircleIcon, EyeIcon, EyeOffIcon, LockIcon, MailIcon } from '../components/icons'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [capsLock, setCapsLock] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{
    title?: string
    detail?: string
    fieldErrors?: Record<string, string[]>
  } | null>(null)

  const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/dashboard'

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await login({ email: email.trim(), password })
      navigate(from, { replace: true })
    } catch (err) {
      setError(err as { title?: string; detail?: string; fieldErrors?: Record<string, string[]> })
    } finally {
      setPending(false)
    }
  }

  return (
    <AuthLayout minimal>
      <h1 className="font-display text-2xl font-bold tracking-tight">Welcome back</h1>
      <p className="mt-1.5 text-sm text-stone-500">
        Sign in to your Achievers Global Consult trading account.
      </p>

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
        <div>
          <Field
            label="Password"
            id="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            required
            icon={<LockIcon className="h-4 w-4" />}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={(e) => setCapsLock(e.getModifierState('CapsLock'))}
            onKeyUp={(e) => setCapsLock(e.getModifierState('CapsLock'))}
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
          <div className="mt-1.5 flex items-center justify-between gap-3">
            {capsLock ? (
              <p className="flex items-center gap-1.5 text-xs font-medium text-amber-700">
                <AlertCircleIcon className="h-3.5 w-3.5" />
                Caps Lock is on.
              </p>
            ) : (
              <span />
            )}
            <Link
              to="/forgot-password"
              className="text-xs font-medium text-stone-500 transition hover:text-brand-700 hover:underline"
            >
              Forgot password?
            </Link>
          </div>
        </div>
        <SubmitButton type="submit" pending={pending}>
          Sign in
        </SubmitButton>

        <p className="text-center text-xs leading-relaxed text-stone-400">
          One sign-in for buyers, suppliers, logistics providers and administrators.
        </p>
      </form>

      <p className="mt-6 border-t border-stone-100 pt-5 text-center text-sm text-stone-500">
        New here?{' '}
        <Link to="/register" className="font-semibold text-brand-700 hover:underline">
          Create an account
        </Link>
      </p>
    </AuthLayout>
  )
}
