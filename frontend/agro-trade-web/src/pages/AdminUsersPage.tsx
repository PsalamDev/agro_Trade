import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Modal } from '../components/overlays'
import { Button, Card, EmptyState, ErrorAlert, PageHeader, Spinner, TextArea } from '../components/ui'
import { UsersIcon } from '../components/icons'
import { adminSearchUsers, adminSetUserStatus } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { count, formatUtc } from '../lib/format'
import { USER_STATUS_ACTION } from '../types/trade'
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
  const [statusTarget, setStatusTarget] = useState<AdminUserDto | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

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

  /**
   * Brings a suspended account back. Reactivation does not take a reason — it is the resolution of
   * a suspension, and the suspension's own reason already says what was wrong.
   */
  async function reactivate(account: AdminUserDto) {
    setStatusError(null)
    setBusyId(account.id)
    try {
      await adminSetUserStatus(account.id, { action: USER_STATUS_ACTION.Activate })
      await load()
    } catch (err) {
      setStatusError(problemFrom(err).detail ?? 'The server refused the change.')
    } finally {
      setBusyId(null)
    }
  }

  function onStatusChanged() {
    setStatusTarget(null)
    void load()
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Users"
          subtitle="Every account on the platform, with the roles it holds and the business it owns. Roles are set by the registration flow; an administrator may suspend an account or bring it back, and each such decision is recorded in the audit trail."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        <div className="mt-6 flex flex-wrap items-end gap-3">
          <label className="flex-1 sm:max-w-sm">
            <span className="mb-1.5 block text-xs font-medium text-stone-700">
              Search name, email, business or buyer ID
            </span>
            <input
              type="search"
              value={term}
              onChange={(e) => applyFilter(setTerm)(e.target.value)}
              placeholder="BUY-ID-AG-000001, or a name"
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

        {statusError ? (
          <div className="mt-4">
            <ErrorAlert title="Could not change the account" detail={statusError} />
          </div>
        ) : null}

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

                      <div className="flex flex-wrap items-center gap-1.5">
                        {!account.isActive ? (
                          <span className="rounded-md bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-700 ring-1 ring-red-200">
                            Suspended
                          </span>
                        ) : null}
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
                      {account.buyerId ? (
                        <div className="flex gap-1.5">
                          <dt>Buyer ID</dt>
                          <dd className="font-medium text-stone-700">{account.buyerId}</dd>
                        </div>
                      ) : null}
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

                    <div className="mt-3 flex justify-end">
                      {account.isActive ? (
                        <button
                          type="button"
                          onClick={() => {
                            setStatusError(null)
                            setStatusTarget(account)
                          }}
                          className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-red-700 ring-1 ring-red-200 transition hover:bg-red-50"
                        >
                          Suspend
                        </button>
                      ) : (
                        <button
                          type="button"
                          disabled={busyId === account.id}
                          onClick={() => void reactivate(account)}
                          className="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-brand-700 ring-1 ring-brand-200 transition hover:bg-brand-50 disabled:opacity-50"
                        >
                          {busyId === account.id ? 'Reactivating…' : 'Reactivate'}
                        </button>
                      )}
                    </div>
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

      {statusTarget ? (
        <SuspendUserDialog
          account={statusTarget}
          onClose={() => setStatusTarget(null)}
          onDone={onStatusChanged}
        />
      ) : null}
    </div>
  )
}

/**
 * Suspends an account.
 *
 * The reason is mandatory here and on the server: a suspension the account cannot read is a state
 * change nobody can act on or appeal, and the reason is what the audit trail keeps.
 */
function SuspendUserDialog({
  account,
  onClose,
  onDone,
}: {
  account: AdminUserDto
  onClose: () => void
  onDone: () => void
}) {
  const [reason, setReason] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    const trimmed = reason.trim()
    if (!trimmed) {
      setError('A suspension needs a reason. It is recorded against the account and in the audit trail.')
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      await adminSetUserStatus(account.id, {
        action: USER_STATUS_ACTION.Suspend,
        reason: trimmed,
      })
      onDone()
    } catch (err) {
      setError(problemFrom(err).detail ?? 'The server refused the change.')
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Suspend an account"
      description={account.email}
      size="md"
      dismissOnBackdrop={!submitting}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={submitting}>
            {submitting ? 'Suspending…' : 'Suspend account'}
          </Button>
        </>
      }
    >
      <p className="text-sm text-stone-600">
        A suspended account is refused at sign-in, and any session already open ends at its next
        request rather than when its token expires. The account is not deleted: the audit trail
        refers to it.
      </p>

      <div className="mt-4">
        <TextArea
          label="Reason"
          id="suspend-reason"
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Why is this account being suspended?"
        />
      </div>

      {error ? (
        <div className="mt-4">
          <ErrorAlert title="Suspension refused" detail={error} />
        </div>
      ) : null}
    </Modal>
  )
}
