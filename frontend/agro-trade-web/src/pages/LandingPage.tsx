import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { COMMODITIES } from '../lib/brand'
import { Eyebrow } from '../components/ui'
import {
  ArrowRightIcon,
  CheckCircleIcon,
  FileTextIcon,
  GlobeIcon,
  PackageIcon,
  ShieldCheckIcon,
  TruckIcon,
} from '../components/icons'

const corridors = [
  { from: 'Nigeria', to: 'Netherlands', crop: 'Cocoa beans · FOB Lagos → CIF Rotterdam' },
  { from: 'Nigeria', to: 'Vietnam', crop: 'Cashew nuts W320 · FOB Apapa → CIF Ho Chi Minh' },
  { from: 'Nigeria', to: 'India', crop: 'Palm kernel · FOB Port Harcourt → CIF Chennai' },
  { from: 'Nigeria', to: 'Taiwan', crop: 'Frozen fish · FOB Lagos → CIF Shanghai' },
]

const pillars = [
  {
    icon: PackageIcon,
    title: 'Origin-graded lots',
    body: 'Every consignment is graded, tested and traceable back to the farm group and harvest window.',
  },
  {
    icon: FileTextIcon,
    title: 'Paperwork handled',
    body: 'Invoice, packing list, certificate of origin and bill of lading prepared alongside your order.',
  },
  {
    icon: TruckIcon,
    title: 'Port-to-port logistics',
    body: 'Booked freight on the agreed Incoterm, with milestone updates between origin and destination.',
  },
  {
    icon: ShieldCheckIcon,
    title: 'Verified counterparties',
    body: 'Businesses are verified before they trade, so quotations come from real, checked operations.',
  },
]

const steps = [
  {
    step: '01',
    title: 'List or publish an RFQ',
    body: 'Exporters publish supply with HS codes, Incoterms, origin and destination ports, and currency. Importers publish requests for quotation targeting any destination.',
  },
  {
    step: '02',
    title: 'Quote, negotiate, close',
    body: 'Suppliers respond with quotations. Compare offers in your own currency, then convert to an order with agreed trade terms.',
  },
  {
    step: '03',
    title: 'Ship and document',
    body: 'Verified trade documents — invoice, packing list, certificate of origin, bill of lading — clear customs with confidence.',
  },
]

export default function LandingPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />

      <section className="relative overflow-hidden">
        <img
          src="/assets/landing-hero.jpg"
          alt=""
          aria-hidden
          className="absolute inset-0 h-full w-full object-cover"
        />
        <div
          aria-hidden
          className="absolute inset-0"
          style={{
            backgroundImage:
              'linear-gradient(90deg, #f7fdf8 0%, #f7fdf8 40%, rgba(247,253,248,0.78) 58%, rgba(247,253,248,0.24) 78%, rgba(247,253,248,0) 97%)',
          }}
        />
        <div className="relative mx-auto grid max-w-6xl gap-12 px-4 py-20 md:grid-cols-2 md:items-center md:py-28">
          <div className="animate-fade-up">
            <Eyebrow>Achievers Global Consult</Eyebrow>
            <h1 className="mt-5 font-display text-4xl leading-[1.08] font-extrabold tracking-tight text-stone-900 sm:text-5xl lg:text-[3.4rem]">
              Cocoa, cashew, palm kernel &amp; fishery —{' '}
              <span className="bg-gradient-to-r from-brand-600 to-brand-800 bg-clip-text text-transparent">
                traded across borders with trust.
              </span>
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-stone-600">
              We import and export agro-allied products from origin farms to world markets — with
              verified quality, clear trade terms, and reliable logistics from port to port.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Link
                to="/register"
                className="inline-flex h-12 items-center gap-2 rounded-xl bg-brand-600 px-6 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:-translate-y-0.5 hover:bg-brand-700"
              >
                Start trading
                <ArrowRightIcon className="h-4 w-4" />
              </Link>
              <a
                href="#how-it-works"
                className="inline-flex h-12 items-center gap-2 rounded-xl border border-stone-300 bg-white/80 px-6 text-sm font-semibold text-stone-700 shadow-soft backdrop-blur transition duration-200 ease-soft hover:border-stone-400 hover:bg-white"
              >
                How it works
              </a>
            </div>
            <ul className="mt-8 flex flex-wrap gap-x-6 gap-y-2 text-sm text-stone-600">
              {['No listing fees for buyers', 'Verified suppliers only', 'FOB · CIF · CFR terms'].map(
                (item) => (
                  <li key={item} className="flex items-center gap-1.5">
                    <CheckCircleIcon className="h-4 w-4 text-brand-600" />
                    {item}
                  </li>
                ),
              )}
            </ul>
          </div>

          <div className="animate-fade-up rounded-3xl border border-stone-200/80 bg-white/80 p-6 shadow-pop backdrop-blur-md">
            <div className="flex items-center justify-between gap-3">
              <h2 className="flex items-center gap-2 text-sm font-semibold tracking-wide text-stone-600 uppercase">
                <GlobeIcon className="h-4 w-4 text-brand-600" />
                Live trade corridors
              </h2>
              <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700">
                <span className="relative flex h-1.5 w-1.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                  <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-emerald-500" />
                </span>
                Active
              </span>
            </div>
            <ul className="mt-4 divide-y divide-stone-100">
              {corridors.map((c) => (
                <li
                  key={`${c.from}-${c.to}`}
                  className="group flex items-center justify-between gap-4 py-3.5 transition"
                >
                  <div className="min-w-0">
                    <p className="font-display flex items-center gap-2 font-semibold">
                      {c.from}
                      <ArrowRightIcon className="h-3.5 w-3.5 text-brand-500 transition duration-300 ease-soft group-hover:translate-x-0.5" />
                      {c.to}
                    </p>
                    <p className="mt-0.5 truncate text-sm text-stone-500">{c.crop}</p>
                  </div>
                  <span className="shrink-0 rounded-lg bg-brand-50 px-2.5 py-1 text-xs font-medium text-brand-700">
                    Shipping
                  </span>
                </li>
              ))}
            </ul>
          </div>
        </div>
        <div className="relative flex h-1 w-full" aria-hidden>
          <div className="w-[91%] bg-brand-600" />
          <div className="w-[9%] bg-red-600" />
        </div>
      </section>

      <section className="border-b border-stone-200 bg-white">
        <div className="mx-auto grid max-w-6xl gap-6 px-4 py-10 sm:grid-cols-3">
          {[
            { value: '4', label: 'Commodity categories' },
            { value: '4', label: 'Active export corridors' },
            { value: 'NG → EU · ASEAN · IN · CN', label: 'Destination markets' },
          ].map((stat) => (
            <div key={stat.label} className="text-center sm:text-left">
              <p className="font-display text-2xl font-extrabold tracking-tight text-brand-700">
                {stat.value}
              </p>
              <p className="mt-1 text-sm text-stone-500">{stat.label}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="mx-auto w-full max-w-6xl px-4 py-20">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-xs font-semibold tracking-widest text-brand-600 uppercase">Our range</p>
            <h2 className="mt-2 font-display text-3xl font-bold tracking-tight">What we trade</h2>
          </div>
          <Link
            to="/marketplace"
            className="group inline-flex items-center gap-1.5 text-sm font-semibold text-brand-700 transition hover:gap-2.5"
          >
            Browse the marketplace
            <ArrowRightIcon className="h-4 w-4" />
          </Link>
        </div>
        <div className="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {COMMODITIES.map((p) => (
            <Link
              key={p.name}
              to="/marketplace"
              className="group overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
            >
              <div className="overflow-hidden bg-stone-100">
                <img
                  src={p.src}
                  alt={p.name}
                  className="h-44 w-full object-cover transition duration-700 ease-soft group-hover:scale-105"
                />
              </div>
              <div className="p-5">
                <h3 className="font-display font-semibold transition group-hover:text-brand-700">
                  {p.name}
                </h3>
                <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{p.blurb}</p>
              </div>
            </Link>
          ))}
        </div>
      </section>

      <section className="border-y border-stone-200 bg-white">
        <div className="mx-auto max-w-6xl px-4 py-20">
          <div className="max-w-2xl">
            <p className="text-xs font-semibold tracking-widest text-brand-600 uppercase">Assurance</p>
            <h2 className="mt-2 font-display text-3xl font-bold tracking-tight">
              Built into every consignment
            </h2>
            <p className="mt-3 text-base leading-relaxed text-stone-600">
              Four things we handle on every lot, regardless of commodity or corridor — so your
              cargo clears without surprises on the other side.
            </p>
          </div>
          <div className="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {pillars.map(({ icon: Icon, title, body }) => (
            <div key={title}>
              <span className="grid h-11 w-11 place-items-center rounded-xl bg-brand-50 text-brand-700 transition duration-300 ease-soft">
                <Icon className="h-5 w-5" />
              </span>
              <h3 className="font-display mt-4 font-semibold">{title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-stone-600">{body}</p>
            </div>
          ))}
          </div>
        </div>
      </section>

      <section id="how-it-works" className="mx-auto w-full max-w-6xl scroll-mt-20 px-4 py-20">
        <div className="text-center">
          <p className="text-xs font-semibold tracking-widest text-brand-600 uppercase">Process</p>
          <h2 className="mt-2 font-display text-3xl font-bold tracking-tight">
            Built for import &amp; export
          </h2>
        </div>
        <div className="mt-12 grid gap-6 md:grid-cols-3">
          {steps.map(({ step, title, body }) => (
            <div
              key={step}
              className="group relative overflow-hidden rounded-2xl border border-stone-200 bg-white p-6 shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
            >
              <span className="font-display absolute -top-4 right-4 text-6xl font-extrabold text-stone-100 transition duration-300 ease-soft group-hover:text-brand-100">
                {step}
              </span>
              <p className="relative text-sm font-bold tracking-widest text-brand-600">{step}</p>
              <h3 className="font-display relative mt-2 text-lg font-semibold">{title}</h3>
              <p className="relative mt-2 text-sm leading-relaxed text-stone-600">{body}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="mx-auto w-full max-w-6xl px-4 pb-20">
        <div className="relative overflow-hidden rounded-3xl bg-gradient-to-br from-brand-700 to-brand-900 px-8 py-14 text-center shadow-brand">
          <div className="bg-grid absolute inset-0 opacity-30" aria-hidden />
          <div className="relative">
            <h2 className="font-display text-3xl font-bold tracking-tight text-white">
              Ready to move your next consignment?
            </h2>
            <p className="mx-auto mt-3 max-w-xl text-brand-100">
              Create a free buyer account to request quotations, compare suppliers, and track your
              shipments end to end.
            </p>
            <div className="mt-8 flex flex-wrap justify-center gap-3">
              <Link
                to="/register"
                className="inline-flex h-12 items-center rounded-xl bg-white px-6 text-sm font-semibold text-brand-800 shadow-lift transition duration-200 ease-soft hover:bg-brand-50"
              >
                Create an account
              </Link>
              <Link
                to="/contact"
                className="inline-flex h-12 items-center rounded-xl border border-white/30 px-6 text-sm font-semibold text-white transition duration-200 ease-soft hover:bg-white/10"
              >
                Talk to the trade desk
              </Link>
            </div>
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  )
}
