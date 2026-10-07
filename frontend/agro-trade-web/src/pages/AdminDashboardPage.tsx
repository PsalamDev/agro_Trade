import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner, Stat } from '../components/ui'
import {
  AlertCircleIcon,
  CheckCircleIcon,
  CoinsIcon,
  GlobeIcon,
  PackageIcon,
  TruckIcon,
  UsersIcon,
} from '../components/icons'
import { getAdminOverview } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, money } from '../lib/format'
import type {
  AdminCommodityStatDto,
  AdminCorridorStatDto,
  AdminFunnelStageDto,
  AdminOverviewDto,
} from '../types/trade'

/**
 * Platform totals per currency, summed from the corridor rows.
 *
 * <p>
 * Grouped, never combined. There is no FX rate anywhere in this system, so a single "total trade
 * value" figure would be a number in no currency at all — and a plausible-looking one, which is
 * worse than a blank. Corridors are summed within a currency; the rows stay separate.
 * </p>
 */
function byCurrency(corridors: AdminCorridorStatDto[]) {
  const totals = new Map<string, { gross: number; settled: number; outstanding: number; orders: number }>()

  for (const corridor of corridors) {
    const existing = totals.get(corridor.currency) ?? {
      gross: 0,
      settled: 0,
      outstanding: 0,
      orders: 0,
    }
    existing.gross += corridor.grossOrderValue
    existing.settled += corridor.netSettled
    existing.outstanding += corridor.outstanding
    existing.orders += corridor.orderCount
    totals.set(corridor.currency, existing)
  }

  return [...totals.entries()].sort((a, b) => b[1].gross - a[1].gross)
}

/**
 * One status breakdown, drawn as bars.
 *
 * <p>
 * Every stage renders, including the empty ones. "3 verified, 1 pending" means nothing on its own;
 * it only becomes a funnel next to "0 rejected", because the gap between stages is the whole reason
 * to draw the shape. A stage with no rows is drawn as an empty track rather than hidden, which is
 * also what makes a genuinely stalled stage visible instead of indistinguishable from a missing one.
 * </p>
 */
function Funnel({
  title,
  caption,
  stages,
}: {
  title: string
  caption: string
  stages: AdminFunnelStageDto[]
}) {
  const total = stages.reduce((sum, stage) => sum + stage.count, 0)
  const peak = stages.reduce((max, stage) => Math.max(max, stage.count), 0)

  return (
    <Card>
      <h2 className="text-sm font-semibold text-stone-800">{title}</h2>
      <p className="mt-0.5 text-xs text-stone-500">{caption}</p>

      {total === 0 ? (
        <p className="mt-4 text-sm text-stone-400">Nothing yet.</p>
      ) : (
        <ul className="mt-4 space-y-2.5">
          {stages.map((stage) => (
            <li key={stage.status}>
              <div className="flex items-baseline justify-between gap-3 text-xs">
                <span className={stage.count === 0 ? 'text-stone-400' : 'text-stone-700'}>
                  {stage.label}
                </span>
                <span className="font-semibold tabular-nums text-stone-800">
                  {count(stage.count)}
                </span>
              </div>
              <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-stone-100">
                <div
                  className={`h-full rounded-full ${stage.count === 0 ? 'bg-stone-200' : 'bg-brand-500'}`}
                  // Peak-relative, not total-relative: scaling to the total would flatten every
                  // stage of a funnel whose early stages are large by definition.
                  style={{ width: `${peak ? Math.max((stage.count / peak) * 100, stage.count ? 4 : 0) : 0}%` }}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

/**
 * Supply against demand for one category.
 *
 * <p>
 * The two columns answer different questions and are kept side by side because neither is legible
 * alone. "We list cocoa and nobody is asking" and "everyone wants cocoa and we list none" are both
 * commercially interesting, and one count per category would show neither — a single number cannot
 * distinguish a category we are strong in from one we have abandoned.
 * </p>
 */
function CommodityRow({ commodity }: { commodity: AdminCommodityStatDto }) {
  // Flagged on *open* RFQs, not all of them. A category whose only RFQ closed years ago is a
  // history, not a live gap, and flagging it would put a false signal on the one panel whose
  // entire value is being worth reading.
  const gap = commodity.publishedCount === 0 && commodity.openRfqCount > 0
  const idle = commodity.openRfqCount === 0 && commodity.publishedCount > 0

  return (
    <li className="px-4 py-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="font-medium text-stone-800">{commodity.categoryName}</p>
        {commodity.rfqCount > 0 ? (
          <p className="text-xs text-stone-400">
            {count(commodity.openRfqCount)} open of {count(commodity.rfqCount)} ever
          </p>
        ) : null}
      </div>

      <div className="mt-2 grid grid-cols-3 gap-3 text-center">
        <div>
          <p className="text-sm font-semibold tabular-nums text-stone-800">
            {count(commodity.publishedCount)}
          </p>
          <p className="text-[11px] text-stone-500">Published</p>
        </div>
        <div>
          <p className="text-sm font-semibold tabular-nums text-stone-800">
            {count(commodity.openRfqCount)}
          </p>
          <p className="text-[11px] text-stone-500">Open RFQs</p>
        </div>
        <div>
          <p className="text-sm font-semibold tabular-nums text-stone-800">
            {count(commodity.productCount - commodity.publishedCount)}
          </p>
          <p className="text-[11px] text-stone-500">Drafts</p>
        </div>
      </div>

      {gap ? (
        <p className="mt-2 inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2 py-1 text-[11px] font-medium text-amber-800">
          <AlertCircleIcon className="h-3.5 w-3.5" />
          In demand now, and nothing listed
        </p>
      ) : null}
      {idle ? (
        <p className="mt-2 inline-flex items-center gap-1.5 rounded-md bg-stone-100 px-2 py-1 text-[11px] font-medium text-stone-600">
          <CheckCircleIcon className="h-3.5 w-3.5" />
          Listed, with no current buyer asking
        </p>
      ) : null}
    </li>
  )
}

export default function AdminDashboardPage() {
  const [overview, setOverview] = useState<AdminOverviewDto | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)
  const [pending, setPending] = useState(false)

  const load = useCallback(async () => {
    setPending(true)
    setError(null)
    try {
      setOverview(await getAdminOverview())
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const currencies = overview ? byCurrency(overview.corridors) : []
  const mismatched = overview?.corridors.reduce((sum, c) => sum + c.mismatchedLedgerEntries, 0) ?? 0

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Operations overview"
          subtitle="One snapshot of the whole platform. Every figure below was read in the same request, so two numbers shown side by side describe the same moment."
          action={
            <button
              type="button"
              onClick={() => void load()}
              disabled={pending}
              className="rounded-lg bg-white px-3.5 py-2 text-sm font-semibold text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-60"
            >
              {pending ? 'Refreshing…' : 'Refresh'}
            </button>
          }
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load the overview" detail={error.detail} />
          </div>
        ) : null}

        {overview === null && error === null ? (
          <div className="mt-8">
            <Spinner label="Loading the overview…" />
          </div>
        ) : null}

        {overview ? (
          <>
            {/* Platform size. */}
            <div className="mt-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Stat
                label="Orders"
                value={count(overview.counts.orders)}
                icon={<PackageIcon className="h-4 w-4" />}
              />
              <Stat
                label="Users"
                value={count(overview.counts.users)}
                icon={<UsersIcon className="h-4 w-4" />}
                tone="neutral"
              />
              <Stat
                label="Consignments"
                value={count(overview.counts.shipments)}
                icon={<TruckIcon className="h-4 w-4" />}
                tone={overview.counts.shipmentsInException > 0 ? 'amber' : 'neutral'}
              />
              <Stat
                label="Settlements"
                value={count(overview.counts.transactions)}
                icon={<CoinsIcon className="h-4 w-4" />}
                tone="neutral"
              />
            </div>

            <p className="mt-2 text-xs text-stone-500">
              {count(overview.counts.verifiedBusinesses)} of {count(overview.counts.businesses)}{' '}
              businesses verified · {count(overview.counts.publishedProducts)} of{' '}
              {count(overview.counts.products)} listings published
              {overview.counts.shipmentsInException > 0 ? (
                <>
                  {' · '}
                  <span className="font-medium text-amber-700">
                    {count(overview.counts.shipmentsInException)} consignment
                    {overview.counts.shipmentsInException === 1 ? '' : 's'} ha
                    {overview.counts.shipmentsInException === 1 ? 's' : 've'} hit an exception
                  </span>
                </>
              ) : null}
            </p>

            {/*
              Trade value. Gross and net are reported as two figures side by side rather than one
              "sales" number, because they answer different questions: gross is what has been
              ordered, net is what has actually arrived. Collapsing them would hide every unpaid
              order in the system, which is the number an administrator most needs to see.
            */}
            <Card className="mt-6">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h2 className="font-display text-base font-semibold text-stone-800">
                    Trade value and settlement
                  </h2>
                  <p className="mt-0.5 max-w-2xl text-xs text-stone-500">
                    Ordered value against money that has actually been received. The gap between them
                    is what buyers still owe.
                  </p>
                </div>
                <span className="inline-flex items-center gap-1.5 rounded-md bg-stone-100 px-2 py-1 text-[11px] font-medium text-stone-600">
                  <GlobeIcon className="h-3.5 w-3.5" />
                  Never converted between currencies
                </span>
              </div>

              {currencies.length === 0 ? (
                <p className="mt-4 text-sm text-stone-400">No orders yet.</p>
              ) : (
                <div className="mt-4 overflow-x-auto">
                  <table className="w-full min-w-[34rem] text-left text-sm">
                    <thead>
                      <tr className="border-b border-stone-200 text-xs text-stone-500">
                        <th scope="col" className="pb-2 font-medium">
                          Currency
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Orders
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Ordered
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Settled
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Outstanding
                        </th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-stone-100">
                      {currencies.map(([currency, totals]) => (
                        <tr key={currency}>
                          <th scope="row" className="py-2.5 font-semibold text-stone-800">
                            {currency}
                          </th>
                          <td className="py-2.5 text-right tabular-nums text-stone-600">
                            {count(totals.orders)}
                          </td>
                          <td className="py-2.5 text-right font-medium tabular-nums text-stone-800">
                            {money(totals.gross, currency)}
                          </td>
                          <td className="py-2.5 text-right tabular-nums text-brand-700">
                            {money(totals.settled, currency)}
                          </td>
                          <td className="py-2.5 text-right tabular-nums text-stone-600">
                            {money(totals.outstanding, currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {mismatched > 0 ? (
                <p className="mt-4 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
                  {count(mismatched)} ledger entr{mismatched === 1 ? 'y was' : 'ies were'}{' '}
                  recorded in a currency other than the order they belong to. Those figures are held
                  out of the totals above rather than folded into a number that reconciles against
                  nothing.
                </p>
              ) : (
                <p className="mt-4 text-xs text-stone-400">
                  These totals are per-currency by construction: the platform stores no exchange
                  rate, so NGN and USD are never added into one figure.
                </p>
              )}
            </Card>

            {/* Funnels. */}
            <div className="mt-6 grid gap-4 md:grid-cols-2">
              <Funnel
                title="Business verification"
                caption="Where registrations sit in review."
                stages={overview.businessVerificationFunnel}
              />
              <Funnel
                title="Document review"
                caption="Paperwork uploaded against orders."
                stages={overview.documentVerificationFunnel}
              />
              <Funnel
                title="Order lifecycle"
                caption="Orders counted by status, including every stage with none."
                stages={overview.orderFunnel}
              />
              <Funnel
                title="Payment status"
                caption="Orders counted by what has been paid, not by payment attempts — one order retried three times is still one obligation."
                stages={overview.paymentFunnel}
              />
            </div>

            {/* Demand. */}
            <Card className="mt-6">
              <h2 className="font-display text-base font-semibold text-stone-800">Buyer demand</h2>
              <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <Stat label="Open RFQs" value={count(overview.demand.openRfqs)} tone="neutral" />
                <Stat
                  label="Unanswered"
                  value={count(overview.demand.unansweredOpenRfqs)}
                  tone={overview.demand.unansweredOpenRfqs > 0 ? 'amber' : 'neutral'}
                />
                <Stat
                  label="Average budget"
                  value={
                    overview.demand.averageBudgetCeiling === null
                      ? '—'
                      : money(overview.demand.averageBudgetCeiling, overview.demand.preferredCurrency)
                  }
                  tone="neutral"
                />
                <Stat
                  label="Requested in"
                  // Empty means nothing is open. Saying "Mixed" there would imply open RFQs in
                  // several currencies, which is a claim about demand the data does not support.
                  value={
                    overview.demand.preferredCurrency === ''
                      ? 'None open'
                      : overview.demand.preferredCurrency
                  }
                  tone="neutral"
                />
              </div>
              {overview.demand.averageBudgetCeiling === null && overview.demand.openRfqs > 0 ? (
                <p className="mt-3 text-xs text-stone-500">
                  Open RFQs ask for more than one currency, so an average budget is withheld rather
                  than computed across incomparable figures. Filter by currency to see each figure
                  on its own.
                </p>
              ) : null}
            </Card>

            {/* Corridors. */}
            <Card className="mt-6">
              <h2 className="font-display text-base font-semibold text-stone-800">
                Trade corridors
              </h2>
              <p className="mt-0.5 text-xs text-stone-500">
                Every corridor the platform trades on, busiest first. Not capped — the route an
                administrator opens this page to check is often the quiet one.
              </p>

              {overview.corridors.length === 0 ? (
                <p className="mt-4 text-sm text-stone-400">No corridors yet.</p>
              ) : (
                <div className="mt-4 overflow-x-auto">
                  <table className="w-full min-w-[42rem] text-left text-sm">
                    <thead>
                      <tr className="border-b border-stone-200 text-xs text-stone-500">
                        <th scope="col" className="pb-2 font-medium">
                          Route
                        </th>
                        <th scope="col" className="pb-2 font-medium">
                          Currency
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Orders
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Ordered
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Settled
                        </th>
                        <th scope="col" className="pb-2 text-right font-medium">
                          Outstanding
                        </th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-stone-100">
                      {overview.corridors.map((corridor) => (
                        <tr key={`${corridor.originPort}|${corridor.destinationCountry}|${corridor.currency}`}>
                          <th scope="row" className="py-2.5 font-medium text-stone-800">
                            {corridor.originPort ?? '—'}
                            <span className="text-stone-400"> → </span>
                            {corridor.destinationCountry ?? '—'}
                          </th>
                          <td className="py-2.5 text-xs text-stone-500">{corridor.currency}</td>
                          <td className="py-2.5 text-right tabular-nums text-stone-600">
                            {count(corridor.orderCount)}
                          </td>
                          <td className="py-2.5 text-right tabular-nums text-stone-800">
                            {money(corridor.grossOrderValue, corridor.currency)}
                          </td>
                          <td className="py-2.5 text-right tabular-nums text-brand-700">
                            {money(corridor.netSettled, corridor.currency)}
                          </td>
                          <td className="py-2.5 text-right tabular-nums text-stone-600">
                            {money(corridor.outstanding, corridor.currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>

            {/* Commodities. */}
            <Card className="mt-6">
              <h2 className="font-display text-base font-semibold text-stone-800">
                Supply against demand
              </h2>
              <p className="mt-0.5 text-xs text-stone-500">
                What is listed beside what buyers are asking for. A category with RFQs and no
                listings is a gap in the catalogue; a category with listings and no RFQs is stock
                nobody is moving. Both are worth seeing, so neither column is dropped.
              </p>

              {overview.commodities.length === 0 ? (
                <div className="mt-4">
                  <EmptyState
                    title="No categories with activity"
                    body="Publish a listing or open an RFQ and the comparison appears here."
                  />
                </div>
              ) : (
                <ul className="mt-4 divide-y divide-stone-100">
                  {overview.commodities.map((commodity) => (
                    <CommodityRow key={commodity.categoryName} commodity={commodity} />
                  ))}
                </ul>
              )}
            </Card>

            <p className="mt-8 text-center text-xs text-stone-400">
              Looking for a specific decision?{' '}
              <Link to="/admin/audit-log" className="font-medium text-brand-700 hover:underline">
                The audit trail
              </Link>{' '}
              records every approval and rejection with the reason given.
            </p>
          </>
        ) : null}
      </main>
    </div>
  )
}
