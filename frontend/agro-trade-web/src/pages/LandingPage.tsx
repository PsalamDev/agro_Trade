import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { COMMODITIES } from '../lib/brand'

const corridors = [
  { from: 'Nigeria', to: 'Netherlands', crop: 'Cocoa beans · FOB Lagos → CIF Rotterdam' },
  { from: 'Nigeria', to: 'Vietnam', crop: 'Cashew nuts W320 · FOB Apapa → CIF Ho Chi Minh' },
  { from: 'Nigeria', to: 'India', crop: 'Palm kernel · FOB Port Harcourt → CIF Chennai' },
  { from: 'Nigeria', to: 'China', crop: 'Frozen fish · FOB Lagos → CIF Shanghai' },
]

export default function LandingPage() {
  return (
    <div className="min-h-screen">
      <AppHeader />

      <section className="relative overflow-hidden">
        <img
          src="/assets/cocoa.jpg"
          alt=""
          aria-hidden
          className="absolute inset-0 h-full w-full object-cover"
        />
        <div
          aria-hidden
          className="absolute inset-0"
          style={{
            backgroundImage:
              'linear-gradient(90deg, #f0fdf4 0%, #f0fdf4 38%, rgba(240,253,244,0.72) 55%, rgba(240,253,244,0.2) 75%, rgba(240,253,244,0) 96%)',
          }}
        />
        <div className="relative mx-auto grid max-w-6xl gap-10 px-4 py-20 md:grid-cols-2 md:items-center">
          <div>
            <p className="mb-3 inline-flex rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-brand-800">
              Achievers Global Consult
            </p>
            <h1 className="text-4xl font-bold leading-tight tracking-tight sm:text-5xl">
              Cocoa, cashew, palm kernel &amp; fishery — traded across borders with trust.
            </h1>
            <p className="mt-4 max-w-xl text-lg text-stone-600">
              Achievers Global Consult imports and exports agro-allied products from origin farms to
              world markets — with verified quality, clear trade terms, and reliable logistics from
              port to port.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link
                to="/register"
                className="rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
              >
                Start trading
              </Link>
              <a
                href="#how-it-works"
                className="rounded-lg border border-stone-300 bg-white px-5 py-3 text-sm font-semibold text-stone-700 transition hover:bg-stone-50"
              >
                How it works
              </a>
            </div>
          </div>

          <div className="rounded-2xl border border-stone-200 bg-white/80 p-6 shadow-sm backdrop-blur">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-stone-500">
              Live trade corridors
            </h2>
            <ul className="divide-y divide-stone-100">
              {corridors.map((c) => (
                <li key={`${c.from}-${c.to}`} className="flex items-baseline justify-between gap-4 py-3">
                  <div>
                    <p className="font-medium">
                      {c.from} <span className="text-stone-400">→</span> {c.to}
                    </p>
                    <p className="text-sm text-stone-500">{c.crop}</p>
                  </div>
                  <span className="shrink-0 rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700">
                    Active
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

      <section className="mx-auto max-w-6xl px-4 py-16">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2 className="text-3xl font-bold tracking-tight">What we trade</h2>
          <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
            Browse the marketplace →
          </Link>
        </div>
        <div className="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {COMMODITIES.map((p) => (
            <div key={p.name} className="overflow-hidden rounded-xl border border-stone-200 bg-white shadow-sm">
              <img src={p.src} alt={p.name} className="h-44 w-full object-cover" />
              <div className="p-4">
                <h3 className="font-semibold">{p.name}</h3>
                <p className="mt-1 text-sm leading-relaxed text-stone-600">{p.blurb}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section id="how-it-works" className="mx-auto max-w-6xl px-4 py-16">
        <h2 className="text-center text-3xl font-bold tracking-tight">Built for import &amp; export</h2>
        <div className="mt-10 grid gap-6 md:grid-cols-3">
          {[
            {
              step: '01',
              title: 'List or publish an RFQ',
              body: 'Exporters publish supply with HS codes, Incoterms, origin/destination ports, and currency. Importers publish requests for quotation targeting any destination.',
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
          ].map(({ step, title, body }) => (
            <div key={step} className="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
              <p className="text-sm font-bold text-brand-600">{step}</p>
              <h3 className="mt-2 text-lg font-semibold">{title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-stone-600">{body}</p>
            </div>
          ))}
        </div>
      </section>

      <SiteFooter />
    </div>
  )
}