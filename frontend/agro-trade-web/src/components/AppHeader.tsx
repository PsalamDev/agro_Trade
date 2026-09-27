import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { NotificationsBell } from './NotificationsBell'
import { COMPANY_ROLE } from '../types/auth'
import {
  BuildingIcon,
  ChevronDownIcon,
  FileTextIcon,
  GridIcon,
  LogOutIcon,
  MenuIcon,
  MessageIcon,
  SearchIcon,
  ShieldCheckIcon,
  XIcon,
} from './icons'

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `relative rounded-lg px-3 py-2 text-sm font-medium transition duration-200 ease-soft after:absolute after:inset-x-3 after:bottom-0 after:h-0.5 after:origin-center after:rounded-full after:bg-brand-600 after:transition-transform after:duration-300 after:ease-soft ${
    isActive
      ? 'text-brand-700 after:scale-x-100'
      : 'text-stone-600 after:scale-x-0 hover:bg-stone-100 hover:text-stone-900 hover:after:scale-x-100'
  }`

const menuItemClass = ({ isActive }: { isActive: boolean }) =>
  `flex items-center gap-2.5 rounded-xl px-3 py-2 text-sm font-medium transition duration-200 ease-soft ${
    isActive
      ? 'bg-brand-50 text-brand-700'
      : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
  }`

function initialsOf(name: string) {
  const parts = name.trim().split(/\s+/).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || '?'
}

export function AppHeader() {
  const { user, logout } = useAuth()
  const roles = user?.roles ?? []
  const isCompany = roles.includes(COMPANY_ROLE)
  const isAdmin = roles.includes('Administrator')
  const [tradingOpen, setTradingOpen] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)

  const tradingLinks = [
    { to: '/rfqs', label: 'Find RFQs', icon: SearchIcon, show: true },
    { to: '/my/rfqs', label: 'My RFQs', icon: FileTextIcon, show: true },
    { to: '/messages', label: 'Messages', icon: MessageIcon, show: true },
    { to: '/my/products', label: 'My listings', icon: GridIcon, show: isCompany },
    { to: '/business', label: 'Business', icon: BuildingIcon, show: true },
    { to: '/admin/verification', label: 'Admin', icon: ShieldCheckIcon, show: isAdmin },
  ].filter((link) => link.show)

  const primaryLinks = [
    { to: '/', label: 'Home', end: true },
    { to: '/marketplace', label: 'Products', end: false },
    { to: '/about', label: 'About', end: false },
    { to: '/contact', label: 'Contact', end: false },
  ]

  function closeMenus() {
    setTradingOpen(false)
    setMobileOpen(false)
  }

  return (
    <header className="sticky top-0 z-40 border-b border-stone-200/80 bg-white/85 backdrop-blur-xl">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4">
        <Link to="/" onClick={closeMenus} className="group flex shrink-0 items-center gap-2.5">
          <span className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-sm font-bold text-white shadow-brand transition duration-300 ease-soft group-hover:scale-105">
            AG
          </span>
          <span className="font-display text-[17px] leading-tight font-bold tracking-tight">
            Achievers <span className="text-brand-600">Global</span>
          </span>
        </Link>

        <nav className="flex min-w-0 items-center gap-1">
          <div className="no-scrollbar flex min-w-0 items-center gap-0.5 overflow-x-auto">
            {primaryLinks.map(({ to, label, end }) => (
              <NavLink key={to} to={to} end={end} className={linkClass}>
                {label}
              </NavLink>
            ))}
          </div>
          {user && tradingLinks.length > 0 ? (
            <div className="relative shrink-0">
              <button
                type="button"
                onClick={() => setTradingOpen((open) => !open)}
                className={`flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium transition duration-200 ease-soft ${
                  tradingOpen ? 'bg-stone-100 text-stone-900' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
                }`}
                aria-expanded={tradingOpen}
                aria-haspopup="menu"
              >
                Trading
                <ChevronDownIcon
                  className={`h-3.5 w-3.5 text-stone-400 transition-transform duration-300 ease-soft ${
                    tradingOpen ? 'rotate-180' : ''
                  }`}
                />
              </button>
              {tradingOpen ? (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setTradingOpen(false)} />
                  <div
                    role="menu"
                    className="absolute right-0 top-full z-20 mt-2 w-56 origin-top-right animate-scale-in rounded-2xl border border-stone-200 bg-white p-1.5 shadow-pop"
                  >
                    <p className="px-3 pt-1.5 pb-2 text-[10px] font-semibold tracking-widest text-stone-400 uppercase">
                      Trading desk
                    </p>
                    {tradingLinks.map((link) => (
                      <NavLink
                        key={link.to}
                        to={link.to}
                        onClick={() => setTradingOpen(false)}
                        className={menuItemClass}
                      >
                        <link.icon className="h-4 w-4 text-stone-400" />
                        {link.label}
                      </NavLink>
                    ))}
                  </div>
                </>
              ) : null}
            </div>
          ) : null}
        </nav>

        <div className="flex shrink-0 items-center gap-1.5">
          {user ? (
            <>
              <NotificationsBell />
              <Link
                to="/dashboard"
                title={user.displayName}
                className="hidden items-center gap-2 rounded-xl px-2 py-1.5 transition hover:bg-stone-100 md:flex"
              >
                <span className="grid h-7 w-7 place-items-center rounded-lg bg-brand-100 text-xs font-bold text-brand-800">
                  {initialsOf(user.displayName)}
                </span>
                <span className="max-w-28 truncate text-sm font-medium text-stone-700">
                  {user.displayName}
                </span>
              </Link>
              <button
                onClick={() => void logout()}
                title="Sign out"
                aria-label="Sign out"
                className="grid h-9 w-9 place-items-center rounded-xl text-stone-500 transition duration-200 ease-soft hover:bg-stone-100 hover:text-stone-900"
              >
                <LogOutIcon className="h-4 w-4" />
              </button>
            </>
          ) : (
            <>
              <NavLink to="/login" className={linkClass}>
                Sign in
              </NavLink>
              <Link
                to="/register"
                className="hidden h-9 items-center rounded-xl bg-brand-600 px-3.5 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700 sm:inline-flex"
              >
                Join the marketplace
              </Link>
            </>
          )}
          <button
            type="button"
            onClick={() => setMobileOpen((open) => !open)}
            aria-label={mobileOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={mobileOpen}
            className="grid h-9 w-9 place-items-center rounded-xl text-stone-600 transition hover:bg-stone-100 hover:text-stone-900 lg:hidden"
          >
            {mobileOpen ? <XIcon className="h-5 w-5" /> : <MenuIcon className="h-5 w-5" />}
          </button>
        </div>
      </div>

      {mobileOpen ? (
        <div className="animate-fade-in border-t border-stone-200 bg-white lg:hidden">
          <div className="mx-auto max-w-6xl space-y-1 px-4 py-4">
            {primaryLinks.map(({ to, label, end }) => (
              <NavLink key={to} to={to} end={end} onClick={closeMenus} className={menuItemClass}>
                {label}
              </NavLink>
            ))}
            {user ? (
              <div className="mt-3 space-y-1 border-t border-stone-100 pt-3">
                {tradingLinks.map((link) => (
                  <NavLink key={link.to} to={link.to} onClick={closeMenus} className={menuItemClass}>
                    <link.icon className="h-4 w-4 text-stone-400" />
                    {link.label}
                  </NavLink>
                ))}
                <Link
                  to="/dashboard"
                  onClick={closeMenus}
                  className="flex items-center gap-2.5 rounded-xl px-3 py-2 text-sm font-medium text-stone-600 transition hover:bg-stone-100"
                >
                  <span className="grid h-5 w-5 place-items-center rounded-md bg-brand-100 text-[10px] font-bold text-brand-800">
                    {initialsOf(user.displayName)}
                  </span>
                  {user.displayName}
                </Link>
              </div>
            ) : (
              <div className="mt-3 space-y-2 border-t border-stone-100 pt-3">
                <NavLink
                  to="/register"
                  onClick={closeMenus}
                  className="flex h-10 items-center justify-center rounded-xl bg-brand-600 text-sm font-semibold text-white shadow-brand"
                >
                  Join the marketplace
                </NavLink>
                <NavLink
                  to="/login"
                  onClick={closeMenus}
                  className="flex h-10 items-center justify-center rounded-xl border border-stone-300 text-sm font-semibold text-stone-700"
                >
                  Sign in
                </NavLink>
              </div>
            )}
          </div>
        </div>
      ) : null}
    </header>
  )
}
