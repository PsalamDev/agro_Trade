import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { getNotifications, markAllNotificationsRead, markNotificationRead } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc } from '../lib/format'
import { NOTIFICATION_TYPES, labelOf } from '../types/trade'
import type { NotificationDto } from '../types/trade'

export default function NotificationsPage() {
  const [items, setItems] = useState<NotificationDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      const result = await getNotifications({ pageSize: 50 })
      setItems(result.items)
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function readOne(n: NotificationDto) {
    if (n.isRead) return
    try {
      await markNotificationRead(n.id)
      setItems((list) => list?.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)) ?? null)
    } catch {
      /* keep unread on failure */
    }
  }

  async function readAll() {
    try {
      await markAllNotificationsRead()
      setItems((list) => list?.map((x) => ({ ...x, isRead: true })) ?? null)
    } catch {
      /* best-effort */
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-3xl px-4 py-10">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Notifications</h1>
            <p className="mt-1 text-sm text-stone-500">
              Orders, shipments, payments, quotations and account activity — newest first.
            </p>
          </div>
          <button
            type="button"
            onClick={() => void readAll()}
            className="rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-50"
          >
            Mark all read
          </button>
        </div>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load notifications" detail={error.detail} />
          </div>
        ) : null}

        {items === null ? (
          <div className="mt-6">
            <Spinner />
          </div>
        ) : items.length === 0 ? (
          <div className="mt-6">
            <EmptyState title="You’re all caught up" body="New activity will show up here." />
          </div>
        ) : (
          <ul className="mt-6 space-y-3">
            {items.map((n) => (
              <li key={n.id}>
                <button
                  type="button"
                  onClick={() => void readOne(n)}
                  className={`w-full rounded-xl border p-4 text-left transition ${
                    n.isRead ? 'border-stone-200 bg-white' : 'border-brand-200 bg-brand-50/50'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-semibold uppercase tracking-wide text-brand-700">
                      {labelOf(NOTIFICATION_TYPES, n.type)}
                    </span>
                    <span className="text-xs text-stone-400">{formatUtc(n.createdAtUtc)}</span>
                  </div>
                  <p className="mt-1 font-medium">{n.title}</p>
                  {n.body ? <p className="mt-0.5 text-sm text-stone-600">{n.body}</p> : null}
                  {n.link ? (
                    <Link to={n.link} className="mt-2 inline-block text-sm font-medium text-brand-700 hover:underline">
                      View
                    </Link>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        )}
      </main>
    </div>
  )
}