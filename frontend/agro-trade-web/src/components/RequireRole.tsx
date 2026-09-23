import { Navigate } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import type { ReactNode } from 'react'

export function RequireRole({ roles, children }: { roles: string[]; children: ReactNode }) {
  const { user, initializing } = useAuth()

  if (initializing) {
    return (
      <div className="grid min-h-screen place-items-center">
        <p className="animate-pulse text-sm text-stone-500">Loading…</p>
      </div>
    )
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  if (!user.roles.some((r) => roles.includes(r))) {
    return <Navigate to="/dashboard" replace />
  }

  return <>{children}</>
}