import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'
import { LockIcon, MailIcon } from '../components/icons'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ title?: string; detail?: string } | null>(null)

  const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/dashboard'

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      await login({ email, password })
      navigate(from, { replace: true })
    } catch (err) {
      setError(err as { title?: string; detail?: string })
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="flex flex-1 items-center justify-center px-4 py-14">
        <div className="w-full max-w-md">
          <div className="animate-fade-up rounded-3xl border border-stone-200 bg-white p-7 shadow-pop sm:p-8">
            <span className="grid h-12 w-12 place-items-center rounded-2xl bg-brand-50 text-brand-700">
              <LockIcon className="h-5 w-5" />
            </span>
            <h1 className="font-display mt-5 text-2xl font-bold tracking-tight">Welcome back</h1>
            <p className="mt-1.5 text-sm text-stone-500">
              Sign in to your Achievers Global Consult trading account.
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
              <Field
                label="Password"
                id="password"
                type="password"
                autoComplete="current-password"
                required
                icon={<LockIcon className="h-4 w-4" />}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
              <SubmitButton type="submit" pending={pending}>
                Sign in
              </SubmitButton>
            </form>

            <div className="mt-6 flex items-center justify-between border-t border-stone-100 pt-5 text-sm">
              <Link to="/register" className="font-semibold text-brand-700 hover:underline">
                New here? Create an account
              </Link>
              <Link
                to="/forgot-password"
                className="text-stone-500 transition hover:text-stone-800 hover:underline"
              >
                Forgot password?
              </Link>
            </div>
          </div>

          <p className="mt-6 text-center text-xs text-stone-400">
            Secured by the Achievers Global Consult trade desk
          </p>
        </div>
      </main>
    </div>
  )
}