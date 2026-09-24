import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { NotificationsBell } from './NotificationsBell'

const SELLER_ROLES = ['Farmer', 'Supplier', 'AgroProcessor', 'Exporter', 'Distributor']

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-2 text-sm font-medium transition ${
    isActive ? 'bg-brand-50 text-brand-700' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
  }`

export function AppHeader() {
  const { user, logout } = useAuth()
  const roles = user?.roles ?? []
  const isSeller = roles.some((r) => SELLER_ROLES.includes(r))
  const isAdmin = roles.includes('Administrator')

  return (
    <header className="sticky top-0 z-10 border-b border-stone-200 bg-white/90 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4">
        <Link to="/" className="flex shrink-0 items-center gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-brand-600 text-sm font-bold text-white">
            AG
          </span>
          <span className="text-lg font-semibold tracking-tight">
            Achievers <span className="text-brand-600">Global</span>
          </span>
        </Link>

        <nav className="flex min-w-0 items-center gap-1 overflow-x-auto">
          <NavLink to="/marketplace" className={linkClass}>
            Marketplace
          </NavLink>
          <NavLink to="/rfqs" className={linkClass}>
            RFQs
          </NavLink>
          {user ? (
            <>
              <NavLink to="/my/rfqs" className={linkClass}>
                My RFQs
              </NavLink>
              <NavLink to="/messages" className={linkClass}>
                Messages
              </NavLink>
              {isSeller ? (
                <NavLink to="/my/products" className={linkClass}>
                  My listings
                </NavLink>
              ) : null}
              <NavLink to="/business" className={linkClass}>
                Business
              </NavLink>
              {isAdmin ? (
                <NavLink to="/admin/verification" className={linkClass}>
                  Admin
                </NavLink>
              ) : null}
            </>
          ) : null}
        </nav>

        <div className="flex shrink-0 items-center gap-1">
          {user ? (
            <>
              <NotificationsBell />
              <span className="hidden max-w-32 truncate px-2 text-sm text-stone-500 md:inline">
                {user.displayName}
              </span>
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
        </div>
      </div>
    </header>
  )
}