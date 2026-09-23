import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import type { ReactNode } from 'react'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user, initializing } = useAuth()
  const location = useLocation()

  if (initializing) {
    return (
      <div className="grid min-h-screen place-items-center">
        <p className="animate-pulse text-sm text-stone-500">Loading…</p>
      </div>
    )
  }

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <>{children}</>
}