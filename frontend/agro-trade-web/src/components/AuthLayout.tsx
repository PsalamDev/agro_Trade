import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ORG } from '../lib/brand'
import { ArrowLeftIcon, FileTextIcon, ShieldCheckIcon, TruckIcon } from './icons'
import { Logo } from './Logo'

const PROOF_POINTS = [
  {
    icon: ShieldCheckIcon,
    title: 'Verified counterparties',
    body: 'Every business is checked before it trades, so quotations come from real, vetted operations.',
  },
  {
    icon: TruckIcon,
    title: 'Port-to-port logistics',
    body: 'Freight booked on the agreed Incoterm, with milestone updates between origin and destination.',
  },
  {
    icon: FileTextIcon,
    title: 'Paperwork handled',
    body: 'Invoice, packing list, certificate of origin and bill of lading prepared alongside your order.',
  },
]

export interface AuthLayoutProps {
  children: ReactNode
  /** Small label above the brand headline on the left panel. */
  eyebrow?: string
  /** Brand headline shown on the left panel (large screens only). */
  headline?: string
  /** Supporting sentence under the brand headline. */
  blurb?: string
  /**
   * Render the brand panel with the logo only — no headline, proof points or
   * commodity strip. Used by sign-in, where the form's own welcome copy is
   * enough and a quiet green panel reads cleaner.
   */
  minimal?: boolean
}

/**
 * Split-screen shell for the unauthenticated pages — sign in, register and the
 * password / email flows. The left panel carries the brand and the trust story;
 * the right column is a clean, focused surface for a single form. Pages pass
 * only their form as children so the whole auth surface stays consistent.
 */
export function AuthLayout({ children, eyebrow, headline, blurb, minimal = false }: AuthLayoutProps) {
  return (
    <div className="flex min-h-screen flex-col lg:grid lg:grid-cols-2">
      <aside className="hidden bg-brand-800 lg:flex lg:flex-col">
        {minimal ? (
          // Sign-in: the mark centred on the brand panel with a short caption.
          <div className="flex flex-1 flex-col items-center justify-center p-10">
            <Link to="/" className="group block w-full max-w-md">
              <Logo
                variant="panel"
                size="xl"
                className="transition duration-300 ease-soft group-hover:opacity-90"
              />
            </Link>

            <div className="mt-10 max-w-sm text-center">
              <p className="font-display text-xl font-semibold tracking-tight text-white sm:text-2xl">
                Trade with confidence.
              </p>
              <p className="mt-3 text-sm leading-relaxed text-brand-100/75">
                Get your agro-allied products — from verified origin farms to buyers across the
                globe, with clear trade terms and reliable logistics.
              </p>
            </div>

            <ul className="mt-8 flex flex-wrap items-center justify-center gap-x-3 gap-y-1 text-[11px] font-semibold tracking-widest text-brand-200/70 uppercase">
              <li>Verified origins</li>
              <li aria-hidden>·</li>
              <li>Port-to-port logistics</li>
              <li aria-hidden>·</li>
              <li>Documented shipments</li>
            </ul>
          </div>
        ) : (
          <div className="flex flex-1 flex-col p-10 xl:p-14">
            <Link to="/" className="group inline-flex items-center gap-2.5 self-start">
              <Logo
                variant="panel"
                size="md"
                className="max-w-[200px] transition duration-300 ease-soft group-hover:opacity-90"
              />
            </Link>

            <div className="flex flex-1 items-center py-12">
              <div className="max-w-md">
              <p className="text-xs font-semibold tracking-widest text-brand-300 uppercase">
                {eyebrow ?? ORG.name}
              </p>
              <h2 className="font-display mt-3 text-3xl leading-tight font-bold tracking-tight text-white xl:text-[2.1rem]">
                {headline ?? 'Agro commodities, traded across borders with trust.'}
              </h2>
              <p className="mt-4 text-sm leading-relaxed text-brand-100/80">
                {blurb ??
                  'Cocoa, cashew, palm kernel and fishery — from verified origin farms to buyers worldwide, with clear trade terms and reliable logistics.'}
              </p>

              <ul className="mt-10 space-y-5">
                {PROOF_POINTS.map(({ icon: Icon, title, body }) => (
                  <li key={title} className="flex gap-3.5">
                    <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/10 text-brand-200 ring-1 ring-white/15">
                      <Icon className="h-5 w-5" />
                    </span>
                    <div>
                      <p className="text-sm font-semibold text-white">{title}</p>
                      <p className="mt-0.5 text-sm leading-relaxed text-brand-100/70">{body}</p>
                    </div>
                  </li>
                ))}
              </ul>
              </div>
            </div>

            <p className="mt-10 text-xs font-medium tracking-widest text-brand-200/60 uppercase">
              Cocoa · Cashew nut · Palm kernel · Fishery
            </p>
          </div>
        )}
      </aside>

      <main className="flex min-h-screen flex-col bg-white">
        <div className="flex items-center justify-between gap-4 px-6 py-5 sm:px-10">
          <Link to="/" className="inline-flex items-center gap-2.5 lg:hidden">
            <Logo variant="horizontal" size="sm" className="max-w-[160px]" />
          </Link>
          <Link
            to="/"
            className="ml-auto inline-flex items-center gap-1.5 rounded-lg text-sm font-medium text-stone-500 transition duration-200 ease-soft hover:text-stone-900"
          >
            <ArrowLeftIcon className="h-4 w-4" />
            Back to home
          </Link>
        </div>

        <div className="flex flex-1 items-center justify-center px-6 pb-14 sm:px-10">
          <div className="animate-fade-up w-full max-w-md py-4">{children}</div>
        </div>

        <footer className="px-6 pb-8 text-center text-xs text-stone-400 sm:px-10 lg:text-left">
          © {new Date().getFullYear()} {ORG.name}. Trade with confidence.
        </footer>
      </main>
    </div>
  )
}
