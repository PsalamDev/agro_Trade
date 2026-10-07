import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Card, EmptyState, ErrorAlert, PageHeader, Spinner } from '../components/ui'
import { UsersIcon } from '../components/icons'
import { adminSearchUsers } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc } from '../lib/format'
import type { AdminUserDto, PagedResult } from '../types/trade'

/**
 * Role names to filter by.
 *
 * <p>
 * The list is hardcoded here rather than fetched: these are the roles the platform recognises, and
 * a filter that offers an unknown role would silently return nothing — which reads as "no such
 * users" rather than "no such filter". Roles are filtered by *name*, not by id, because the ids
 * are database-generated and no client can learn them.
 * </p>
 */
const ROLE_FILTERS = [
  { value: '', label: 'All roles' },
  { value: 'Company', label: 'Company (sellers)' },
  { value: 'Importer', label: 'Importers' },
  { value: 'Exporter', label: 'Exporters' },
  { value: 'Administrator', label: 'Administrators' },
]

export default function AdminUsersPage() {
  const [role, setRole] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<PagedResult<AdminUserDto> | null>(null)
  const [error, setError] = useState<{ detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setResult(
        await adminSearchUsers({
          q: term.trim() || undefined,
          role: role || undefined,
          page,
          pageSize: 25,
        }),
      )
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [role, term, page])

  useEffect(() => {
    void load()
  }, [load])

  function applyFilter(setter: (value: string) => void) {
    return (value: string) => {
      setter(value)
      setPage(1)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Users"
          subtitle="Every account on the platform, with the roles it holds and the business it owns. This queue is read-only — roles and standing are changed by the registration flow and by an administrator acting on a case, not from this screen."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search name, email or business
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="company@achieversglobalconsult.com"
              className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            />
          </label>

          <label>
            <span className="mb-1.5 block text-xs font-medium text-stone-700">Role</span>
            <select
              value={role}
              onChange={(e) => applyFilter(setRole)(e.target.value)}
              className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-soft transition focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
            >
              {ROLE_FILTERS.map((filter) => (
                <option key={filter.value} value={filter.value}>
                  {filter.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load users" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6">
          {result === null && error === null ? (
            <Spinner label="Loading users…" />
          ) : result!.items.length === 0 ? (
            <EmptyState
              title="No matching accounts"
              body="Try a different role, or clear the search."
              icon={<UsersIcon className="h-5 w-5" />}
            />
          ) : (
            <>
              <p className="mb-2 text-xs text-stone-500">
                {count(result!.total)} account{result!.total === 1 ? '' : 's'}, newest registrations
                first
              </p>

              <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
                {result!.items.map((account) => (
                  <li key={account.id} className="bg-white px-4 py-3.5">
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-medium text-stone-800">
                          {account.displayName || '—'}
                        </p>
                        <p className="truncate text-xs text-stone-500">{account.email}</p>
                      </div>

                      <div className="flex flex-wrap gap-1.5">
                        {account.roles.map((name) => (
                          <span
                            key={name}
                            className="rounded-md bg-stone-100 px-2 py-0.5 text-[11px] font-medium text-stone-600"
                          >
                            {name}
                          </span>
                        ))}
                      </div>
                    </div>

                    <dl className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-xs text-stone-500">
                      <div className="flex gap-1.5">
                        <dt>Business</dt>
                        <dd className="font-medium text-stone-700">
                          {account.businessName ?? 'None registered'}
                        </dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Registered</dt>
                        {/*
                          An unstamped registration time shows as unknown rather than as a
                          plausible-looking date. Rows created before timestamps were recorded are
                          genuinely undated, and inventing one would be worse than admitting it.
                        */}
                        <dd className="font-medium text-stone-700">
                          {account.createdAtUtc ? formatUtc(account.createdAtUtc) : 'Not recorded'}
                        </dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt>Email</dt>
                        <dd className="font-medium text-stone-700">
                          {account.emailConfirmed ? 'Confirmed' : 'Unconfirmed'}
                        </dd>
                      </div>
                      {account.lockedOut ? (
                        <div className="flex gap-1.5">
                          <dt>Standing</dt>
                          <dd className="font-medium text-amber-700">Locked out</dd>
                        </div>
                      ) : null}
                    </dl>
                  </li>
                ))}
              </ul>

              {result!.totalPages > 1 ? (
                <div className="mt-4 flex items-center justify-between text-sm">
                  <button
                    type="button"
                    disabled={page <= 1}
                    onClick={() => setPage((p) => p - 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    ← Previous
                  </button>
                  <span className="text-xs text-stone-500">
                    Page {page} of {result!.totalPages}
                  </span>
                  <button
                    type="button"
                    disabled={page >= result!.totalPages}
                    onClick={() => setPage((p) => p + 1)}
                    className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-stone-700 ring-1 ring-stone-200 transition hover:bg-stone-50 disabled:opacity-50"
                  >
                    Next →
                  </button>
                </div>
              ) : null}
            </>
          )}
        </div>

        <Card className="mt-8 px-4 py-3.5">
          <p className="text-xs text-stone-500">
            The audit trail records the decisions an administrator makes, and every entry names the
            account that made it — including entries whose actor has since been deleted, so a
            decision cannot be orphaned from its author by removing the account.
          </p>
        </Card>
      </main>
    </div>
  )
}
