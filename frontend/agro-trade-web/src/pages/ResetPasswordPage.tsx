import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, problemFrom } from '../lib/api'
import { ErrorAlert, Field, SubmitButton } from '../components/ui'
import { AppHeader } from '../components/AppHeader'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const [email, setEmail] = useState(params.get('email') ?? '')
  const [token, setToken] = useState(params.get('token') ?? '')
  const [newPassword, setNewPassword] = useState('')
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
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto flex max-w-md flex-col px-4 py-16">
        <h1 className="text-2xl font-bold tracking-tight">Choose a new password</h1>
        {done ? (
          <div className="mt-8 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
            <p className="font-medium">Password updated.</p>
            <Link to="/login" className="mt-3 inline-block font-semibold text-emerald-700 hover:underline">
              Sign in with your new password
            </Link>
          </div>
        ) : (
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
              label="Reset token"
              id="token"
              required
              value={token}
              onChange={(e) => setToken(e.target.value)}
            />
            <Field
              label="New password"
              id="newPassword"
              type="password"
              autoComplete="new-password"
              required
              minLength={8}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
            <SubmitButton type="submit" pending={pending}>
              Set new password
            </SubmitButton>
          </form>
        )}
      </main>
    </div>
  )
}