import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'

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
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto flex max-w-md flex-col px-4 py-16">
        <h1 className="text-2xl font-bold tracking-tight">Sign in to AgroTrade</h1>
        <p className="mt-1 text-sm text-stone-500">Access your trading account.</p>

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
          <Field
            label="Password"
            id="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <SubmitButton type="submit" pending={pending}>
            Sign in
          </SubmitButton>
        </form>

        <div className="mt-6 flex items-center justify-between text-sm">
          <Link to="/register" className="font-medium text-brand-700 hover:underline">
            New here? Create an account
          </Link>
          <Link to="/forgot-password" className="text-stone-500 hover:underline">
            Forgot password?
          </Link>
        </div>
      </main>
    </div>
  )
}