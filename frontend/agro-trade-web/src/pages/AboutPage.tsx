import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { COMMODITIES } from '../lib/brand'
import { Eyebrow } from '../components/ui'
import { ArrowRightIcon, FileTextIcon, ShieldCheckIcon, TruckIcon } from '../components/icons'

const values = [
  {
    icon: ShieldCheckIcon,
    title: 'Verified sourcing',
    body: 'Every consignment is traceable to origin — graded, tested, and documented before it ships.',
  },
  {
    icon: FileTextIcon,
    title: 'Clear trade terms',
    body: 'Incoterms, ports, payment terms, and quality specifications are agreed upfront. No surprises at the border.',
  },
  {
    icon: TruckIcon,
    title: 'Reliable logistics',
    body: 'From inland farms to destination ports, we coordinate freight, documentation, and customs clearance.',
  },
]

export default function AboutPage() {
  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />

      <section className="border-b border-stone-200 bg-gradient-to-b from-brand-50 to-stone-50">
        <div className="mx-auto max-w-6xl px-4 py-20">
          <Eyebrow>About us</Eyebrow>
          <h1 className="font-display mt-5 max-w-3xl text-4xl leading-[1.08] font-extrabold tracking-tight sm:text-5xl">
            Achievers Global Consult
          </h1>
          <p className="mt-5 max-w-2xl text-lg leading-relaxed text-stone-600">
            We are an importer and exporter of agro-allied products — cocoa, cashew nuts, palm kernel,
            and fishery — connecting origin farms to importers and processors around the world.
          </p>
        </div>
      </section>

      <section className="mx-auto w-full max-w-6xl px-4 py-20">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-xs font-semibold tracking-widest text-brand-600 uppercase">Our range</p>
            <h2 className="font-display mt-2 text-3xl font-bold tracking-tight">What we trade</h2>
          </div>
          <Link
            to="/marketplace"
            className="group inline-flex items-center gap-1.5 text-sm font-semibold text-brand-700 transition hover:gap-2.5"
          >
            Browse products
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
          <div className="text-center">
            <p className="text-xs font-semibold tracking-widest text-brand-600 uppercase">Standards</p>
            <h2 className="font-display mt-2 text-3xl font-bold tracking-tight">
              Why trade with us
            </h2>
          </div>
          <div className="mt-12 grid gap-6 md:grid-cols-3">
            {values.map(({ icon: Icon, title, body }) => (
              <div
                key={title}
                className="rounded-2xl border border-stone-200 bg-white p-6 shadow-card transition duration-300 ease-soft hover:-translate-y-1 hover:border-brand-200 hover:shadow-lift"
              >
                <span className="grid h-11 w-11 place-items-center rounded-xl bg-brand-50 text-brand-700">
                  <Icon className="h-5 w-5" />
                </span>
                <h3 className="font-display mt-4 text-lg font-semibold">{title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-stone-600">{body}</p>
              </div>
            ))}
          </div>
          <div className="mt-12 text-center">
            <Link
              to="/contact"
              className="inline-flex h-12 items-center gap-2 rounded-xl bg-brand-600 px-6 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:-translate-y-0.5 hover:bg-brand-700"
            >
              Contact us
              <ArrowRightIcon className="h-4 w-4" />
            </Link>
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  )
}
