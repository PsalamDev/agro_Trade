import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { COMMODITIES } from '../lib/brand'

const values = [
  {
    title: 'Verified sourcing',
    body: 'Every consignment is traceable to origin — graded, tested, and documented before it ships.',
  },
  {
    title: 'Clear trade terms',
    body: 'Incoterms, ports, payment terms, and quality specifications are agreed upfront. No surprises at the border.',
  },
  {
    title: 'Reliable logistics',
    body: 'From inland farms to destination ports, we coordinate freight, documentation, and customs clearance.',
  },
]

export default function AboutPage() {
  return (
    <div className="min-h-screen">
      <AppHeader />

      <section className="bg-gradient-to-b from-brand-50 to-stone-50">
        <div className="mx-auto max-w-6xl px-4 py-20">
          <p className="mb-3 inline-flex rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-brand-800">
            About us
          </p>
          <h1 className="max-w-2xl text-4xl font-bold leading-tight tracking-tight sm:text-5xl">
            Achievers Global Consult
          </h1>
          <p className="mt-4 max-w-2xl text-lg text-stone-600">
            We are an importer and exporter of agro-allied products — cocoa, cashew nuts, palm kernel, and
            fishery — connecting origin farms to importers and processors around the world.
          </p>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-4 py-16">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2 className="text-3xl font-bold tracking-tight">What we trade</h2>
          <Link to="/marketplace" className="text-sm font-medium text-brand-700 hover:underline">
            Browse products →
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

      <section className="border-t border-stone-100 bg-stone-50">
        <div className="mx-auto max-w-6xl px-4 py-16">
          <h2 className="text-center text-3xl font-bold tracking-tight">Why trade with us</h2>
          <div className="mt-10 grid gap-6 md:grid-cols-3">
            {values.map((v) => (
              <div key={v.title} className="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
                <h3 className="text-lg font-semibold">{v.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-stone-600">{v.body}</p>
              </div>
            ))}
          </div>
          <div className="mt-10 text-center">
            <Link
              to="/contact"
              className="inline-block rounded-lg bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
            >
              Contact us
            </Link>
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  )
}