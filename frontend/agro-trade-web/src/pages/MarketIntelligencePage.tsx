import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import CountrySelect from '../components/CountrySelect'
import {
  Button,
  EmptyState,
  ErrorAlert,
  PageHeader,
  SelectField,
  Skeleton,
} from '../components/ui'
import { problemFrom } from '../lib/api'
import { formatUtc, money } from '../lib/format'
import { countryName, getMarketPrices } from '../lib/trade'
import type { MarketDataDto } from '../types/trade'
import { CoinsIcon, FilterIcon, GlobeIcon, XIcon } from '../components/icons'

const SERIES_COLORS = ['#0f766e', '#b45309', '#1d4ed8', '#be123c', '#7c3aed', '#475569']

interface SeriesPoint {
  id: string
  /** Wire timestamp, unmodified — used verbatim for tooltips and labels. */
  date: string
  dateMs: number
  price: number
  region: string | null
  source: string
}

/** One trend line: a single commodity observed from one origin, in one unit and currency. */
interface Series {
  key: string
  commodity: string
  country: string
  currency: string
  unit: string
  points: SeriesPoint[]
}

/** Δ over the previous observation as a percentage, with direction. */
function Delta({ value }: { value: number }) {
  if (!Number.isFinite(value) || value === 0) {
    return <span className="text-xs font-semibold text-stone-400">— 0.0%</span>
  }
  const up = value > 0
  return (
    <span
      className={`inline-flex items-center gap-1 text-xs font-semibold ${up ? 'text-emerald-600' : 'text-rose-600'}`}
    >
      <svg
        viewBox="0 0 24 24"
        className={`h-3 w-3 ${up ? '' : 'rotate-180'}`}
        fill="none"
        stroke="currentColor"
        strokeWidth={2.5}
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M12 19V5M5 12l7-7 7 7" />
      </svg>
      {up ? '+' : '−'}
      {Math.abs(value).toFixed(1)}%
    </span>
  )
}

/** Day-only label, read in the zone the server recorded. */
function shortDate(iso: string): string {
  const parsed = new Date(iso)
  if (Number.isNaN(parsed.getTime())) return iso
  return parsed.toLocaleDateString(undefined, { day: 'numeric', month: 'short', timeZone: 'UTC' })
}

/** Compact axis label — the chart does not have room for a full `money()` string. */
function priceLabel(value: number, currency: string): string {
  const compact = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
  return `${currency.toUpperCase()} ${compact}`
}

function TrendChart({ series }: { series: Series[] }) {
  const points = series.flatMap((s) => s.points)
  if (points.length === 0) return null

  const times = points.map((p) => p.dateMs)
  const prices = points.map((p) => p.price)
  const minT = Math.min(...times)
  const maxT = Math.max(...times)
  const rawMin = Math.min(...prices)
  const rawMax = Math.max(...prices)
  const spread = rawMax - rawMin
  const pad = spread === 0 ? Math.max(1, rawMax * 0.05) : spread * 0.08
  const yMin = rawMin - pad
  const yMax = rawMax + pad
  const ySpan = yMax - yMin || 1
  const tSpan = maxT - minT || 1

  const W = 720
  const H = 240
  const L = 52
  const R = 14
  const T = 18
  const B = 30
  const x = (t: number) => L + ((t - minT) / tSpan) * (W - L - R)
  const y = (p: number) => T + (1 - (p - yMin) / ySpan) * (H - T - B)
  const base = H - B
  const currency = series[0].currency
  const commodity = series[0].commodity
  const single = series.length === 1

  const first = points.reduce((a, b) => (a.dateMs <= b.dateMs ? a : b))
  const last = points.reduce((a, b) => (a.dateMs >= b.dateMs ? a : b))

  const ticks = [0, 1, 2, 3].map((i) => {
    const v = yMax - (i * ySpan) / 3
    return { v, yy: y(v) }
  })

  return (
    <div>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        className="mt-4 h-60 w-full"
        role="img"
        aria-label={`${commodity} price trend`}
      >
        {ticks.map(({ v, yy }) => (
          <g key={v}>
            <line x1={L} x2={W - R} y1={yy} y2={yy} stroke="currentColor" className="text-stone-200" strokeDasharray="3 4" />
            <text x={L - 8} y={yy + 3.5} textAnchor="end" className="fill-stone-400 text-[10px]">
              {priceLabel(v, currency)}
            </text>
          </g>
        ))}
        <text x={L} y={H - 10} className="fill-stone-400 text-[10px]">
          {shortDate(first.date)}
        </text>
        <text x={W - R} y={H - 10} textAnchor="end" className="fill-stone-400 text-[10px]">
          {shortDate(last.date)}
        </text>

        {series.map((s, idx) => {
          const color = SERIES_COLORS[idx % SERIES_COLORS.length]
          const line = s.points
            .map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.dateMs)},${y(p.price)}`)
            .join(' ')
          return (
            <g key={s.key}>
              {single ? (
                <path
                  d={`${line} L${x(last.dateMs)},${base} L${x(first.dateMs)},${base} Z`}
                  fill={color}
                  opacity={0.12}
                />
              ) : null}
              <path d={line} fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
              {s.points.map((p) => (
                <circle key={p.id} cx={x(p.dateMs)} cy={y(p.price)} r={3.5} fill={color} stroke="#fff" strokeWidth={1.5}>
                  <title>{`${commodity} · ${countryName(s.country)}${p.region ? ` · ${p.region}` : ''}\n${formatUtc(p.date)}\n${money(p.price, currency)}`}</title>
                </circle>
              ))}
            </g>
          )
        })}
      </svg>

      <div className="mt-4 flex flex-wrap gap-x-6 gap-y-2">
        {series.map((s, idx) => {
          const color = SERIES_COLORS[idx % SERIES_COLORS.length]
          const lastPoint = s.points[s.points.length - 1]
          const prevPoint = s.points[s.points.length - 2]
          const delta =
            prevPoint && prevPoint.price !== 0
              ? ((lastPoint.price - prevPoint.price) / prevPoint.price) * 100
              : null
          return (
            <div key={s.key} className="flex items-center gap-2 text-sm">
              <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: color }} />
              <span className="font-medium text-stone-700">
                {countryName(s.country)}
                {s.points[0].region ? ` - ${s.points[0].region}` : ''}
              </span>
              <span className="text-stone-500">
                {money(lastPoint.price, s.currency)}/{s.unit.toLowerCase()}
              </span>
              {delta === null ? (
                <span className="text-xs text-stone-400">single observation</span>
              ) : (
                <Delta value={delta} />
              )}
              <span className="text-xs text-stone-400">{s.points.length} obs</span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

/**
 * Public market intelligence (Phase 3): seeded commodity price series rendered as trend charts.
 *
 * The page fetches the whole curated feed once and groups it locally — a trend line is per
 * commodity × origin × unit × currency, which is a shape no single server query returns, and the
 * feed is small by design. Filters refine the same rows the chart and table both read, so the
 * three never disagree about what the current selection is.
 */
export default function MarketIntelligencePage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [rows, setRows] = useState<MarketDataDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const commodityParam = searchParams.get('commodity') ?? ''
  const country = searchParams.get('country') ?? ''
  const currency = searchParams.get('currency') ?? ''

  useEffect(() => {
    let cancelled = false
    getMarketPrices()
      .then((result) => {
        if (!cancelled) setRows(result.items)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const commodities = useMemo(() => [...new Set(rows.map((r) => r.commodity))].sort(), [rows])
  const currencies = useMemo(() => [...new Set(rows.map((r) => r.currency))].sort(), [rows])

  /**
   * Default to the first commodity so a fresh visitor lands on a chart, not an empty pane. Clear
   * filters snaps back here too — the URL stays clean and the page still shows something useful.
   */
  const defaultCommodity = commodities[0] ?? ''
  const selectedCommodity = commodityParam || defaultCommodity

  const filtered = useMemo(
    () =>
      rows.filter(
        (r) => (!country || r.country === country) && (!currency || r.currency === currency),
      ),
    [rows, country, currency],
  )

  const cards = useMemo(() => {
    const byCommodity = new Map<string, MarketDataDto[]>()
    for (const row of filtered) {
      const list = byCommodity.get(row.commodity) ?? []
      list.push(row)
      byCommodity.set(row.commodity, list)
    }
    const result: {
      commodity: string
      observations: number
      origins: number
      points: MarketDataDto[]
    }[] = []
    for (const [name, points] of byCommodity) {
      points.sort((a, b) => new Date(a.reportedAtUtc).getTime() - new Date(b.reportedAtUtc).getTime())
      result.push({
        commodity: name,
        observations: points.length,
        origins: new Set(points.map((p) => p.country)).size,
        points,
      })
    }
    return result.sort(
      (a, b) => b.observations - a.observations || a.commodity.localeCompare(b.commodity),
    )
  }, [filtered])

  const series = useMemo(() => {
    const map = new Map<string, Series>()
    for (const row of filtered) {
      if (row.commodity !== selectedCommodity) continue
      const key = `${row.country}|${row.currency}|${row.unit}`
      let s = map.get(key)
      if (!s) {
        s = {
          key,
          commodity: row.commodity,
          country: row.country,
          currency: row.currency,
          unit: row.unit,
          points: [],
        }
        map.set(key, s)
      }
      s.points.push({
        id: row.id,
        date: row.reportedAtUtc,
        dateMs: new Date(row.reportedAtUtc).getTime(),
        price: row.price,
        region: row.region,
        source: row.source,
      })
    }
    const list = [...map.values()]
    for (const s of list) s.points.sort((a, b) => a.dateMs - b.dateMs)
    list.sort((a, b) => {
      const aLast = a.points[a.points.length - 1].price
      const bLast = b.points[b.points.length - 1].price
      return bLast - aLast
    })
    return list
  }, [filtered, selectedCommodity])

  const selectedCard = cards.find((c) => c.commodity === selectedCommodity)

  const tableRows = useMemo(
    () =>
      [...filtered]
        .sort((a, b) => new Date(b.reportedAtUtc).getTime() - new Date(a.reportedAtUtc).getTime())
        .slice(0, 100),
    [filtered],
  )

  const hasFilters = Boolean(commodityParam || country || currency)

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearchParams(next)
  }

  function clearAll() {
    setSearchParams(new URLSearchParams())
  }

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Market intelligence"
          title="Commodity price benchmarks"
          subtitle="Curated price series from field desks, FAO and ICCO — compare trend lines by commodity, origin and currency."
          action={
            hasFilters ? (
              <Button variant="secondary" size="sm" onClick={clearAll}>
                <XIcon className="h-3.5 w-3.5" />
                Clear filters
              </Button>
            ) : undefined
          }
        />

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load market data" detail={error.detail} />
          </div>
        ) : null}

        {loading ? (
          <div className="mt-6 space-y-4">
            <Skeleton className="h-12 w-full rounded-2xl" />
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-36 rounded-2xl" />
              ))}
            </div>
            <Skeleton className="h-72 rounded-2xl" />
          </div>
        ) : rows.length === 0 ? (
          <div className="mt-8">
            <EmptyState
              title="No price series yet"
              body="An administrator will publish the first commodity benchmarks here."
              icon={<GlobeIcon className="h-5 w-5" />}
            />
          </div>
        ) : (
          <>
            <form className="mt-6 rounded-2xl border border-stone-200 bg-white p-5 shadow-card">
              <p className="mb-4 flex items-center gap-2 text-sm font-medium text-stone-700">
                <FilterIcon className="h-4 w-4 text-stone-400" />
                Filter the series
              </p>
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <SelectField
                  label="Commodity"
                  id="mi-commodity"
                  options={commodities}
                  values={commodities}
                  value={selectedCommodity}
                  onChange={(e) => void updateParam('commodity', e.target.value)}
                />
                <CountrySelect
                  label="Origin"
                  id="mi-country"
                  value={country}
                  onChange={(code) => void updateParam('country', code)}
                  allowClear
                  placeholder="Any origin"
                  hint="Search by country name — the ISO-2 code is applied automatically."
                />
                <SelectField
                  label="Currency"
                  id="mi-currency"
                  options={currencies}
                  values={currencies}
                  value={currency}
                  onChange={(e) => void updateParam('currency', e.target.value)}
                />
              </div>
            </form>

            {filtered.length === 0 ? (
              <div className="mt-8">
                <EmptyState
                  title="No observations match your filters"
                  body="Try a different origin or currency, or clear the filters to see every benchmark."
                  icon={<GlobeIcon className="h-5 w-5" />}
                  action={
                    <Button variant="secondary" onClick={clearAll}>
                      <XIcon className="h-4 w-4" />
                      Clear filters
                    </Button>
                  }
                />
              </div>
            ) : (
              <>
                {/* Commodity cards — one per commodity in the current selection. */}
                <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {cards.map((card) => {
                    const latestRow = card.points[card.points.length - 1]
                    const prevRow = card.points[card.points.length - 2]
                    const delta =
                      prevRow && prevRow.price !== 0
                        ? ((latestRow.price - prevRow.price) / prevRow.price) * 100
                        : null
                    const active = card.commodity === selectedCommodity
                    return (
                      <button
                        key={card.commodity}
                        type="button"
                        onClick={() => void updateParam('commodity', card.commodity)}
                        className={`rounded-2xl border bg-white p-5 text-left shadow-soft transition duration-200 ease-soft ${
                          active
                            ? 'border-brand-500 ring-2 ring-brand-500/20'
                            : 'border-stone-200 hover:border-brand-300 hover:shadow-card'
                        }`}
                      >
                        <div className="flex items-start justify-between gap-2">
                          <p className="font-display text-base font-semibold text-stone-800">{card.commodity}</p>
                          {active ? (
                            <span className="rounded-full bg-brand-100 px-2 py-0.5 text-[10px] font-semibold tracking-wide text-brand-800 uppercase">
                              Showing
                            </span>
                          ) : null}
                        </div>
                        <p className="mt-3 text-xl font-bold tracking-tight text-stone-900">
                          {money(latestRow.price, latestRow.currency)}
                          <span className="ml-1.5 text-xs font-medium text-stone-400">
                            / {latestRow.unit.toLowerCase()}
                          </span>
                        </p>
                        <div className="mt-2">
                          {delta === null ? (
                            <span className="text-xs font-semibold text-stone-400">Single observation</span>
                          ) : (
                            <Delta value={delta} />
                          )}
                        </div>
                        <p className="mt-3 text-xs text-stone-500">
                          {card.observations} obs · {card.origins} origin{card.origins === 1 ? '' : 's'} · latest{' '}
                          {shortDate(latestRow.reportedAtUtc)}
                        </p>
                      </button>
                    )
                  })}
                </div>

                {selectedCard ? (
                  <section className="mt-8 rounded-2xl border border-stone-200 bg-white p-5 shadow-card">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <h2 className="flex items-center gap-2 font-display text-lg font-bold tracking-tight text-stone-900">
                        <CoinsIcon className="h-4 w-4 text-brand-600" />
                        Price trend — {selectedCommodity}
                      </h2>
                      <span className="text-sm text-stone-500">
                        {series.length} line{series.length === 1 ? '' : 's'} · each origin its own series
                      </span>
                    </div>
                    {series.length === 0 ? (
                      <p className="mt-4 text-sm text-stone-500">
                        No observations for {selectedCommodity} in the current filters.
                      </p>
                    ) : (
                      <TrendChart series={series} />
                    )}
                  </section>
                ) : null}

                <section className="mt-8">
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <h2 className="font-display text-lg font-bold tracking-tight text-stone-900">
                      Observations
                    </h2>
                    <span className="text-sm text-stone-500">
                      {tableRows.length} of {filtered.length} — newest first
                    </span>
                  </div>
                  <div className="mt-3 overflow-x-auto rounded-2xl border border-stone-200 bg-white shadow-card">
                    <table className="w-full min-w-[760px] text-left text-sm">
                      <thead>
                        <tr className="border-b border-stone-200 bg-stone-50 text-xs tracking-wide text-stone-500 uppercase">
                          <th className="px-4 py-3 font-semibold">Date (UTC)</th>
                          <th className="px-4 py-3 font-semibold">Commodity</th>
                          <th className="px-4 py-3 font-semibold">Origin</th>
                          <th className="px-4 py-3 font-semibold">Region</th>
                          <th className="px-4 py-3 text-right font-semibold">Price</th>
                          <th className="px-4 py-3 font-semibold">Source</th>
                          <th className="px-4 py-3 font-semibold">Notes</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-stone-100">
                        {tableRows.map((row) => (
                          <tr key={row.id} className="transition hover:bg-stone-50">
                            <td className="px-4 py-3 whitespace-nowrap text-stone-600">
                              {formatUtc(row.reportedAtUtc)}
                            </td>
                            <td className="px-4 py-3 font-medium text-stone-800">{row.commodity}</td>
                            <td className="px-4 py-3 text-stone-600">{countryName(row.country)}</td>
                            <td className="px-4 py-3 text-stone-500">{row.region ?? '—'}</td>
                            <td className="px-4 py-3 text-right font-semibold text-stone-800">
                              {money(row.price, row.currency)}
                              <span className="ml-1 text-xs font-normal text-stone-400">
                                /{row.unit.toLowerCase()}
                              </span>
                            </td>
                            <td className="px-4 py-3 text-stone-500">{row.source}</td>
                            <td className="px-4 py-3 text-stone-500">{row.notes ?? '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              </>
            )}
          </>
        )}
      </main>
    </div>
  )
}