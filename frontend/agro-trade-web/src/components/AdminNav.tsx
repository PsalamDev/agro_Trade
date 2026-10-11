import { Link, useLocation } from 'react-router-dom'
import {
  ChartIcon,
  ClockIcon,
  CoinsIcon,
  FileTextIcon,
  GridIcon,
  InboxIcon,
  PackageIcon,
  SettingsIcon,
  ShieldCheckIcon,
  TagIcon,
  TruckIcon,
  UsersIcon,
} from './icons'

/**
 * The admin screens, in the order an administrator moves through them.
 *
 * <p>
 * A nav strip rather than five more header links. The header already carried two admin entries
 * with no overview above them; adding the dashboard and three queues would have put seven items in
 * a bar with room for a handful, and the queues are only ever reached *from* the overview. One
 * header entry, and the dashboard carries onward navigation.
 * </p>
 */
const DESTINATIONS = [
  {
    to: '/admin',
    label: 'Overview',
    body: 'Trade value, funnels and platform health.',
    icon: <GridIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/verification',
    label: 'Business verification',
    body: 'Approve or reject supplier registrations.',
    icon: <ShieldCheckIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/documents',
    label: 'Document review',
    body: 'Verify the paperwork behind each order.',
    icon: <FileTextIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/products',
    label: 'Products',
    body: 'Every listing, and what the platform has hidden.',
    icon: <TagIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/rfqs',
    label: 'RFQs',
    body: 'Buyer demand, and how much of it has been answered.',
    icon: <InboxIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/orders',
    label: 'Orders',
    body: 'Every order on the platform, paid and unpaid.',
    icon: <PackageIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/shipments',
    label: 'Consignments',
    body: 'Tracking state for every shipment in flight.',
    icon: <TruckIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/transactions',
    label: 'Ledger',
    body: 'Every movement of money, reconciled against the bank.',
    icon: <CoinsIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/reports',
    label: 'Reports',
    body: 'Corridors, demand, verification and supply, over a window.',
    icon: <ChartIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/users',
    label: 'Users',
    body: 'Accounts, roles and the businesses they own.',
    icon: <UsersIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/audit-log',
    label: 'Audit trail',
    body: 'Who decided what, and when.',
    icon: <ClockIcon className="h-4 w-4" />,
  },
  {
    to: '/admin/settings',
    label: 'Settings',
    body: 'Platform controls that running code actually reads.',
    icon: <SettingsIcon className="h-4 w-4" />,
  },
]

export function AdminNav() {
  // react-router's location, not window.location: the latter does not re-render on navigation, so
  // the strip would keep highlighting whichever screen the admin first opened.
  const { pathname } = useLocation()

  return (
    <nav aria-label="Admin sections" className="flex flex-wrap gap-2">
      {DESTINATIONS.map((destination) => {
        // Exact for the overview. A prefix match would light up "Overview" on every admin screen,
        // since "/admin/orders" starts with "/admin".
        const active =
          pathname === destination.to ||
          (destination.to !== '/admin' && pathname.startsWith(`${destination.to}/`))

        return (
          <Link
            key={destination.to}
            to={destination.to}
            aria-current={active ? 'page' : undefined}
            title={destination.body}
            className={`inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-xs font-medium transition ${
              active
                ? 'bg-brand-600 text-white'
                : 'bg-white text-stone-600 ring-1 ring-stone-200 hover:bg-stone-50'
            }`}
          >
            {destination.icon}
            {destination.label}
          </Link>
        )
      })}
    </nav>
  )
}
