import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../lib/auth-context'
import { useCart } from '../lib/cart-context'
import { NotificationsBell } from './NotificationsBell'
import { Dropdown, MenuItem, Offcanvas, Tooltip } from './overlays'
import { BUYER_ROLES, COMPANY_ROLE } from '../types/auth'
import {
  BuildingIcon,
  CartIcon,
  ChevronDownIcon,
  FileTextIcon,
  GridIcon,
  LogOutIcon,
  MenuIcon,
  MessageIcon,
  PackageIcon,
  SearchIcon,
  TruckIcon,
} from './icons'

const linkClass = ({ isActive }: { isActive: boolean }) =>
  `relative rounded-lg px-3 py-2 text-sm font-medium transition duration-200 ease-soft after:absolute after:inset-x-3 after:bottom-0 after:h-0.5 after:origin-center after:rounded-full after:bg-brand-600 after:transition-transform after:duration-300 after:ease-soft ${
    isActive
      ? 'text-brand-700 after:scale-x-100'
      : 'text-stone-600 after:scale-x-0 hover:bg-stone-100 hover:text-stone-900 hover:after:scale-x-100'
  }`

const railItemClass = ({ isActive }: { isActive: boolean }) =>
  `flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm font-medium transition duration-200 ease-soft ${
    isActive ? 'bg-brand-50 text-brand-700' : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'
  }`

function initialsOf(name: string) {
  const parts = name.trim().split(/\s+/).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || '?'
}

export function AppHeader() {
  const { user, logout } = useAuth()
  const { totalQuantity } = useCart()
  const roles = user?.roles ?? []
  const isCompany = roles.includes(COMPANY_ROLE)
  const isAdmin = roles.includes('Administrator')
  const isBuyer = roles.some((role) => BUYER_ROLES.includes(role))
  const isLsp = roles.includes('LogisticsProvider')
  const [menuOpen, setMenuOpen] = useState(false)

  const tradingLinks = [
    { to: '/rfqs', label: 'Find RFQs', icon: <SearchIcon className="h-4 w-4" />, show: true },
    { to: '/my/rfqs', label: 'My RFQs', icon: <FileTextIcon className="h-4 w-4" />, show: true },
    { to: '/dashboard/orders', label: 'Orders', icon: <PackageIcon className="h-4 w-4" />, show: true },
    { to: '/messages', label: 'Messages', icon: <MessageIcon className="h-4 w-4" />, show: true },
    { to: '/my/products', label: 'My listings', icon: <GridIcon className="h-4 w-4" />, show: isCompany },
    { to: '/business', label: 'Business', icon: <BuildingIcon className="h-4 w-4" />, show: true },
    { to: '/logistics/bookings', label: 'Logistics bookings', icon: <TruckIcon className="h-4 w-4" />, show: true },
    { to: '/logistics/profile', label: 'My LSP profile', icon: <BuildingIcon className="h-4 w-4" />, show: isLsp },
    // One admin entry, not six. The header had a link to each of two queues with no overview above
    // them; the dashboard now carries onward navigation to every queue, including these two.
    { to: '/admin', label: 'Admin', icon: <GridIcon className="h-4 w-4" />, show: isAdmin },
  ].filter((link) => link.show)

  const primaryLinks = [
    { to: '/', label: 'Home', end: true },
    { to: '/marketplace', label: 'Products', end: false },
    { to: '/market-intelligence', label: 'Market data', end: false },
    { to: '/logistics', label: 'Logistics', end: false },
    { to: '/about', label: 'About', end: false },
    { to: '/contact', label: 'Contact', end: false },
  ]

  return (
    <header className="sticky top-0 z-40 border-b border-stone-200/80 bg-white/85 backdrop-blur-xl">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4">
        <Link to="/" onClick={() => setMenuOpen(false)} className="group flex shrink-0 items-center gap-2.5">
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
            <Dropdown
              label="Open trading menu"
              header="Trading desk"
              width="w-56"
              buttonClassName="flex shrink-0 items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium text-stone-600 transition duration-200 ease-soft hover:bg-stone-100 hover:text-stone-900"
              button={
                <>
                  Trading
                  <ChevronDownIcon className="h-3.5 w-3.5 text-stone-400" />
                </>
              }
            >
              {tradingLinks.map((link) => (
                <MenuItem key={link.to} to={link.to} icon={link.icon}>
                  {link.label}
                </MenuItem>
              ))}
            </Dropdown>
          ) : null}
        </nav>

        <div className="flex shrink-0 items-center gap-1.5">
          {user ? (
            <>
              {isBuyer ? (
                <Link
                  to="/cart"
                  title="Shopping cart"
                  className="relative grid h-9 w-9 place-items-center rounded-xl text-stone-500 transition duration-200 ease-soft hover:bg-stone-100 hover:text-stone-900"
                >
                  <CartIcon className="h-4.5 w-4.5" />
                  {totalQuantity > 0 ? (
                    <span className="absolute -top-0.5 -right-0.5 grid h-4 min-w-4 place-items-center rounded-full bg-brand-600 px-1 text-[10px] font-bold text-white ring-2 ring-white">
                      {totalQuantity > 9 ? '9+' : totalQuantity}
                    </span>
                  ) : null}
                </Link>
              ) : null}
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
              <Tooltip label="Sign out">
                <button
                  onClick={() => void logout()}
                  aria-label="Sign out"
                  className="grid h-9 w-9 place-items-center rounded-xl text-stone-500 transition duration-200 ease-soft hover:bg-stone-100 hover:text-stone-900"
                >
                  <LogOutIcon className="h-4 w-4" />
                </button>
              </Tooltip>
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
            onClick={() => setMenuOpen(true)}
            aria-label="Open menu"
            className="grid h-9 w-9 place-items-center rounded-xl text-stone-600 transition hover:bg-stone-100 hover:text-stone-900 lg:hidden"
          >
            <MenuIcon className="h-5 w-5" />
          </button>
        </div>
      </div>

      <Offcanvas open={menuOpen} onClose={() => setMenuOpen(false)} title="Menu" width="max-w-xs">
        <nav className="space-y-1">
          {primaryLinks.map(({ to, label, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              onClick={() => setMenuOpen(false)}
              className={railItemClass}
            >
              {label}
            </NavLink>
          ))}
        </nav>

        {user ? (
          <div className="mt-4 space-y-1 border-t border-stone-100 pt-4">
            <p className="px-3 pb-1 text-[10px] font-semibold tracking-widest text-stone-400 uppercase">
              Trading desk
            </p>
            {tradingLinks.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                onClick={() => setMenuOpen(false)}
                className={railItemClass}
              >
                {link.icon}
                {link.label}
              </NavLink>
            ))}
            <Link
              to="/dashboard"
              onClick={() => setMenuOpen(false)}
              className="flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm font-medium text-stone-600 transition hover:bg-stone-100"
            >
              <span className="grid h-5 w-5 place-items-center rounded-md bg-brand-100 text-[10px] font-bold text-brand-800">
                {initialsOf(user.displayName)}
              </span>
              {user.displayName}
            </Link>
            <button
              type="button"
              onClick={() => {
                setMenuOpen(false)
                void logout()
              }}
              className="flex w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm font-medium text-stone-600 transition hover:bg-stone-100"
            >
              <LogOutIcon className="h-4 w-4 text-stone-400" />
              Sign out
            </button>
          </div>
        ) : (
          <div className="mt-4 space-y-2 border-t border-stone-100 pt-4">
            <Link
              to="/register"
              onClick={() => setMenuOpen(false)}
              className="flex h-10 items-center justify-center rounded-xl bg-brand-600 text-sm font-semibold text-white shadow-brand"
            >
              Join the marketplace
            </Link>
            <Link
              to="/login"
              onClick={() => setMenuOpen(false)}
              className="flex h-10 items-center justify-center rounded-xl border border-stone-300 text-sm font-semibold text-stone-700"
            >
              Sign in
            </Link>
          </div>
        )}
      </Offcanvas>
    </header>
  )
}
