import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../lib/auth'

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-2 text-sm font-medium transition ${
    isActive ? 'bg-brand-50 text-brand-700' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
  }`

export function AppHeader() {
  const { user, logout } = useAuth()

  return (
    <header className="sticky top-0 z-10 border-b border-stone-200 bg-white/90 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
        <Link to="/" className="flex items-center gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-brand-600 text-sm font-bold text-white">
            A
          </span>
          <span className="text-lg font-semibold tracking-tight">
            Agro<span className="text-brand-600">Trade</span>
          </span>
        </Link>

        <nav className="flex items-center gap-1">
          {user ? (
            <>
              <NavLink to="/dashboard" className={linkClass}>
                Dashboard
              </NavLink>
              <span className="hidden px-2 text-sm text-stone-500 sm:inline">{user.displayName}</span>
              <button
                onClick={() => void logout()}
                className="rounded-md px-3 py-2 text-sm font-medium text-stone-600 transition hover:bg-stone-100 hover:text-stone-900"
              >
                Sign out
              </button>
            </>
          ) : (
            <>
              <NavLink to="/login" className={linkClass}>
                Sign in
              </NavLink>
              <NavLink
                to="/register"
                className="rounded-md bg-brand-600 px-3 py-2 text-sm font-semibold text-white transition hover:bg-brand-700"
              >
                Join the marketplace
              </NavLink>
            </>
          )}
        </nav>
      </div>
    </header>
  )
}