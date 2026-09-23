import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { RfqCard } from '../components/RfqCard'
import { EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { getMyRfqs } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { useAuth } from '../lib/auth-context'
import type { TradeRequestCardDto } from '../types/trade'

const SELLER_ROLES = ['Farmer', 'Supplier', 'AgroProcessor', 'Exporter', 'Distributor']
const BUYER_ROLES = ['Buyer', 'Importer', 'Distributor']

export default function MyRfqsPage() {
  const { user } = useAuth()
  const [as_, setAs] = useState<'buyer' | 'supplier'>('buyer')
  const [rfqs, setRfqs] = useState<TradeRequestCardDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const roles = user?.roles ?? []
  const canSell = roles.some((r) => SELLER_ROLES.includes(r))
  const canBuy = roles.some((r) => BUYER_ROLES.includes(r))

  const load = useCallback(async () => {
    setError(null)
    try {
      const result = await getMyRfqs({ as: as_, pageSize: 50 })
      setRfqs(result.items)
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [as_])

  useEffect(() => {
    void load()
  }, [load])

  const effectiveAs = !canSell ? 'buyer' : !canBuy ? 'supplier' : as_

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">My RFQs</h1>
            <p className="mt-1 text-sm text-stone-500">
              {effectiveAs === 'buyer' ? "Requests you published — track quotations." : 'RFQs you quoted on — manage your offers.'}
            </p>
          </div>
          {effectiveAs === 'buyer' ? (
            <Link
              to="/rfqs/new"
              className="rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
            >
              Publish RFQ
            </Link>
          ) : null}
        </div>

        {canSell && canBuy ? (
          <div className="mt-6 flex gap-2">
            <button
              type="button"
              onClick={() => setAs('buyer')}
              className={`rounded-lg px-4 py-2 text-sm font-medium transition ${effectiveAs === 'buyer' ? 'bg-brand-600 text-white' : 'bg-white text-stone-600 hover:bg-stone-100'}`}
            >
              As buyer
            </button>
            <button
              type="button"
              onClick={() => setAs('supplier')}
              className={`rounded-lg px-4 py-2 text-sm font-medium transition ${effectiveAs === 'supplier' ? 'bg-brand-600 text-white' : 'bg-white text-stone-600 hover:bg-stone-100'}`}
            >
              As supplier
            </button>
          </div>
        ) : null}

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load your RFQs" detail={error.detail} />
          </div>
        ) : null}

        {rfqs === null ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : rfqs.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="Nothing here yet"
              body={effectiveAs === 'buyer' ? 'Publish a sourcing request to start receiving quotes.' : 'Quote on an open RFQ from the directory and it will appear here.'}
            />
          </div>
        ) : (
          <div className="mt-6 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {rfqs.map((rfq) => (
              <RfqCard key={rfq.id} rfq={rfq} />
            ))}
          </div>
        )}
      </main>
    </div>
  )
}