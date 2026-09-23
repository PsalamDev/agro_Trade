import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'

const corridors = [
  { from: 'Kenya', to: 'Nigeria', crop: 'Cashew kernels · FOB Mombasa → CIF Lagos' },
  { from: 'Ghana', to: 'Netherlands', crop: 'Cocoa beans · FOB Tema → CFR Rotterdam' },
  { from: 'Vietnam', to: 'Senegal', crop: 'Rice · FOB Saigon → CIF Dakar' },
  { from: 'Ethiopia', to: 'UAE', crop: 'Sesame seed · FOB Djibouti → CFR Jebel Ali' },
]

export default function LandingPage() {
  return (
    <div className="min-h-screen">
      <AppHeader />

      <section className="bg-gradient-to-b from-brand-50 to-stone-50">
        <div className="mx-auto grid max-w-6xl gap-10 px-4 py-20 md:grid-cols-2 md:items-center">
          <div>
            <p className="mb-3 inline-flex rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-brand-800">
              Cross-border agrifood trade
            </p>
            <h1 className="text-4xl font-bold leading-tight tracking-tight sm:text-5xl">
              Buy and sell agricultural produce across borders — with trust baked in.
            </h1>
            <p className="mt-4 max-w-xl text-lg text-stone-600">
              AgroTrade connects verified exporters, importers, processors, and logistics providers on
              one corridor-neutral marketplace. Trade in your currency, on your Incoterms, from origin
              port to destination port.
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

          <div className="rounded-2xl border border-stone-200 bg-white p-6 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-stone-500">
              Live corridors
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

      <footer className="border-t border-stone-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-col items-center gap-2 px-4 py-8 text-sm text-stone-500 sm:flex-row sm:justify-between">
          <p>© {new Date().getFullYear()} AgroTrade. Global agrifood trade platform.</p>
          <p>Corridor-neutral · Multi-currency · Trust-first</p>
        </div>
      </footer>
    </div>
  )
}