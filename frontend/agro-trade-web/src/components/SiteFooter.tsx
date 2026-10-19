import { Link } from 'react-router-dom'
import { ORG } from '../lib/brand'
import { MailIcon, MapPinIcon, PhoneIcon } from './icons'

const exploreLinks = [
  { to: '/marketplace', label: 'Marketplace' },
  { to: '/about', label: 'About us' },
  { to: '/contact', label: 'Contact' },
  { to: '/register', label: 'Create an account' },
]

export function SiteFooter() {
  return (
    <footer className="mt-auto border-t border-stone-200 bg-white">
      <div className="mx-auto max-w-6xl px-4 py-14">
        <div className="grid gap-10 sm:grid-cols-2 lg:grid-cols-4">
          <div className="lg:col-span-2">
            <div className="flex items-center gap-2.5">
              <img
                src="/App _ social icon (1).png"
                alt=""
                className="h-9 w-9 rounded-xl object-cover shadow-brand"
              />
              <span className="font-display text-[17px] font-bold tracking-tight">
                Achievers <span className="text-brand-600">Global</span>
              </span>
            </div>
            <p className="mt-4 max-w-sm text-sm leading-relaxed text-stone-500">
              {ORG.tagline} Cocoa, cashew nuts, palm kernel and fishery moved from verified origin
              farms to buyers worldwide, with clear trade terms and reliable logistics.
            </p>
            <p className="mt-4 text-xs font-medium tracking-widest text-stone-400 uppercase">
              Cocoa · Cashew nut · Palm kernel · Fishery
            </p>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-stone-900">Explore</h2>
            <ul className="mt-4 space-y-2.5">
              {exploreLinks.map((link) => (
                <li key={link.to}>
                  <Link
                    to={link.to}
                    className="text-sm text-stone-500 transition hover:text-brand-700"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h2 className="text-sm font-semibold text-stone-900">Trade desk</h2>
            <ul className="mt-4 space-y-3">
              <li>
                <a
                  href={`mailto:${ORG.email}`}
                  className="flex items-center gap-2.5 text-sm text-stone-500 transition hover:text-brand-700"
                >
                  <MailIcon className="h-4 w-4 shrink-0 text-stone-400" />
                  <span className="break-all">{ORG.email}</span>
                </a>
              </li>
              <li className="flex items-center gap-2.5 text-sm text-stone-500">
                <PhoneIcon className="h-4 w-4 shrink-0 text-stone-400" />
                {ORG.phone}
              </li>
              <li className="flex items-center gap-2.5 text-sm text-stone-500">
                <MapPinIcon className="h-4 w-4 shrink-0 text-stone-400" />
                {ORG.address}
              </li>
            </ul>
          </div>
        </div>

        <div className="mt-12 flex flex-col items-center justify-between gap-2 border-t border-stone-100 pt-6 text-sm text-stone-400 sm:flex-row">
          <p>© {new Date().getFullYear()} {ORG.name}. All rights reserved.</p>
          <p>Trade with confidence — verified listings, documented shipments.</p>
        </div>
      </div>
    </footer>
  )
}
