import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { RfqCard } from '../components/RfqCard'
import { Tabs } from '../components/overlays'
import { EmptyState, ErrorAlert, PageHeader, SkeletonGrid } from '../components/ui'
import { getMyRfqs } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import { BUYER_ROLES, COMPANY_ROLE } from '../types/auth'
import type { TradeRequestCardDto } from '../types/trade'

export default function MyRfqsPage() {
  const { user } = useAuth()
  const [as_, setAs] = useState<'buyer' | 'supplier'>('buyer')
  const [rfqs, setRfqs] = useState<TradeRequestCardDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const roles = user?.roles ?? []
  const canSell = roles.includes(COMPANY_ROLE)
  const canBuy = roles.some((role) => BUYER_ROLES.includes(role))

  // A user with one side only never sees the switch, so the fetch follows the side that applies
  // rather than the tab state they cannot reach.
  const effectiveAs = !canSell ? 'buyer' : !canBuy ? 'supplier' : as_

  const load = useCallback(async () => {
    setError(null)
    setRfqs(null)
    try {
      const result = await getMyRfqs({ as: effectiveAs, pageSize: 50 })
      setRfqs(result.items)
    } catch (err) {
      setError(problemFrom(err))
      setRfqs([])
    }
  }, [effectiveAs])

  useEffect(() => {
    void load()
  }, [load])

  const isBuyerView = effectiveAs === 'buyer'
  const tabs = [
    ...(canBuy ? [{ id: 'buyer', label: 'As buyer' }] : []),
    ...(canSell ? [{ id: 'supplier', label: 'As supplier' }] : []),
  ]

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Sourcing"
          title="My RFQs"
          subtitle={
            isBuyerView
              ? 'Requests you published — track quotations and accept an offer.'
              : 'RFQs you quoted on — manage your offers and negotiate with the buyer.'
          }
          action={
            isBuyerView ? (
              <Link
                to="/rfqs/new"
                className="inline-flex h-10 shrink-0 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-brand transition duration-200 ease-soft hover:bg-brand-700"
              >
                Publish RFQ
              </Link>
            ) : undefined
          }
        />

        {tabs.length > 1 ? (
          <div className="mt-6">
            <Tabs items={tabs} value={effectiveAs} onChange={(id) => setAs(id as 'buyer' | 'supplier')} />
          </div>
        ) : null}

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load your RFQs" detail={error.detail} />
          </div>
        ) : null}

        {rfqs === null ? (
          <SkeletonGrid className="mt-6" />
        ) : rfqs.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="Nothing here yet"
              body={
                isBuyerView
                  ? 'Publish a sourcing request to start receiving quotes.'
                  : 'Quote on an open RFQ from the directory and it will appear here.'
              }
              action={
                isBuyerView ? (
                  <Link
                    to="/rfqs/new"
                    className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-brand-700"
                  >
                    Publish an RFQ
                  </Link>
                ) : (
                  <Link
                    to="/rfqs"
                    className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-brand-700"
                  >
                    Browse open RFQs
                  </Link>
                )
              }
            />
          </div>
        ) : (
          <>
            <p className="mt-6 text-sm text-stone-500">
              <span className="rounded-full bg-brand-100 px-3 py-1 text-sm font-semibold text-brand-800">
                {rfqs.length} RFQ{rfqs.length === 1 ? '' : 's'}
              </span>
            </p>
            <div className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {rfqs.map((rfq) => (
                <RfqCard key={rfq.id} rfq={rfq} />
              ))}
            </div>
          </>
        )}
      </main>
    </div>
  )
}
