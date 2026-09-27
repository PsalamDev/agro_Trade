import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { NotificationsBell } from './NotificationsBell'
import { COMPANY_ROLE } from '../types/auth'

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `rounded-md px-3 py-2 text-sm font-medium transition ${
    isActive ? 'bg-brand-50 text-brand-700' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
  }`

export function AppHeader() {
  const { user, logout } = useAuth()
  const roles = user?.roles ?? []
  const isCompany = roles.includes(COMPANY_ROLE)
  const isAdmin = roles.includes('Administrator')
  const [tradingOpen, setTradingOpen] = useState(false)

  const tradingLinks = [
    { to: '/rfqs', label: 'Find RFQs', show: true },
    { to: '/my/rfqs', label: 'My RFQs', show: true },
    { to: '/messages', label: 'Messages', show: true },
    { to: '/my/products', label: 'My listings', show: isCompany },
    { to: '/business', label: 'Business', show: true },
    { to: '/admin/verification', label: 'Admin', show: isAdmin },
  ].filter((link) => link.show)

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
          <NavLink to="/" className={linkClass} end>
            Home
          </NavLink>
          <NavLink to="/marketplace" className={linkClass}>
            Products
          </NavLink>
          <NavLink to="/about" className={linkClass}>
            About
          </NavLink>
          <NavLink to="/contact" className={linkClass}>
            Contact
          </NavLink>
          {user && tradingLinks.length > 0 ? (
            <div className="relative">
              <button
                type="button"
                onClick={() => setTradingOpen((open) => !open)}
                className="relative z-20 flex items-center gap-1 rounded-md px-3 py-2 text-sm font-medium text-stone-600 transition hover:bg-stone-100 hover:text-stone-900"
                aria-expanded={tradingOpen}
              >
                Trading <span className="text-xs text-stone-400">▾</span>
              </button>
              {tradingOpen ? (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setTradingOpen(false)} />
                  <div className="absolute right-0 top-full z-20 mt-1 w-48 rounded-lg border border-stone-200 bg-white p-1.5 shadow-lg">
                    {tradingLinks.map((link) => (
                      <NavLink
                        key={link.to}
                        to={link.to}
                        onClick={() => setTradingOpen(false)}
                        className={({ isActive }) =>
                          `block rounded-md px-3 py-2 text-sm font-medium transition ${
                            isActive ? 'bg-brand-50 text-brand-700' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
                          }`
                        }
                      >
                        {link.label}
                      </NavLink>
                    ))}
                  </div>
                </>
              ) : null}
            </div>
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