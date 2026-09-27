import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { VerificationPill, VerifiedBadge } from '../components/badges'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { getMyBusinesses } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { BUSINESS_TYPES, labelOf } from '../types/trade'
import type { BusinessSummaryDto } from '../types/trade'
import { useAuth } from '../lib/auth-context'
import { COMPANY_ROLE } from '../types/auth'

export default function BusinessHubPage() {
  const { user } = useAuth()
  const [businesses, setBusinesses] = useState<BusinessSummaryDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  useEffect(() => {
    let cancelled = false
    getMyBusinesses()
      .then((list) => {
        if (!cancelled) setBusinesses(list)
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
    return () => {
      cancelled = true
    }
  }, [])

  const isCompany = (user?.roles ?? []).includes(COMPANY_ROLE)

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-4xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Business profiles</h1>
            <p className="mt-1 text-sm text-stone-500">
              Verified profiles unlock export listings, RFQ quoting, and buyer trust.
            </p>
          </div>
          <Link
            to="/business/new"
            className="rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700"
          >
            Register a business
          </Link>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load your businesses" detail={error.detail} />
          </div>
        ) : null}

        {businesses === null ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : businesses.length === 0 ? (
          <div className="mt-6">
            <EmptyState
              title="No business profiles yet"
              body="Register your trading entity to build buyer trust, source imports, and participate in RFQs."
            />
          </div>
        ) : (
          <ul className="mt-6 grid gap-4 sm:grid-cols-2">
            {businesses.map((business) => (
              <li key={business.id}>
                <Card className="flex h-full flex-col">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <Link to={`/business/${business.id}`} className="text-lg font-semibold text-brand-700 hover:underline">
                        {business.tradingName}
                      </Link>
                      <p className="text-sm text-stone-500">
                        {labelOf(BUSINESS_TYPES, business.businessType)} · {business.legalName}
                      </p>
                    </div>
                    <div className="flex items-center gap-2">
                      <VerificationPill status={business.verificationStatus} />
                      <VerifiedBadge visible={business.isVerifiedBadgeVisible} />
                    </div>
                  </div>
                  {business.exportsTo.length > 0 || business.importsFrom.length > 0 ? (
                    <div className="mt-4 flex flex-wrap gap-2 text-xs">
                      {business.exportsTo.length > 0 ? (
                        <span className="rounded-full bg-brand-50 px-2 py-0.5 font-medium text-brand-800">
                          Exports to {business.exportsTo.join(', ')}
                        </span>
                      ) : null}
                      {business.importsFrom.length > 0 ? (
                        <span className="rounded-full bg-blue-50 px-2 py-0.5 font-medium text-blue-800">
                          Imports from {business.importsFrom.join(', ')}
                        </span>
                      ) : null}
                    </div>
                  ) : null}
                  <div className="mt-4 flex-1" />
                  <Link
                    to={`/business/${business.id}`}
                    className="mt-4 inline-block rounded-lg border border-stone-300 px-3 py-2 text-center text-sm font-medium text-stone-700 transition hover:bg-stone-50"
                  >
                    Manage profile &amp; verification
                  </Link>
                </Card>
              </li>
            ))}
          </ul>
        )}

        {isCompany && businesses && businesses.length > 0 ? (
          <p className="mt-8 text-sm text-stone-500">
            Selected the right entity? Create an export listing on the{' '}
            <Link to="/my/products" className="font-medium text-brand-700 hover:underline">
              My listings
            </Link>{' '}
            page.
          </p>
        ) : null}
      </main>
    </div>
  )
}