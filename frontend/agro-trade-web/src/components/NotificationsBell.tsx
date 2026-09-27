import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { getNotifications, getUnreadNotificationCount, markAllNotificationsRead, markNotificationRead } from '../lib/trade'
import { NOTIFICATION_TYPES, labelOf } from '../types/trade'
import type { NotificationDto } from '../types/trade'
import { Spinner } from './ui'
import { BellIcon } from './icons'

function timeAgo(iso: string): string {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000))
  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

export function NotificationsBell() {
  const [open, setOpen] = useState(false)
  const [unread, setUnread] = useState(0)
  const [items, setItems] = useState<NotificationDto[] | null>(null)
  const [busy, setBusy] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let cancelled = false
    getUnreadNotificationCount()
      .then((count) => {
        if (!cancelled) setUnread(count)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [open])

  useEffect(() => {
    function handlePointerDown(event: PointerEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [])

  async function toggle() {
    if (!open) {
      setOpen(true)
      try {
        const result = await getNotifications({ unreadOnly: !!unread, pageSize: 8 })
        setItems(result.items)
      } catch {
        setItems([])
      }
    } else {
      setOpen(false)
    }
  }

  async function openOne(id: string) {
    setBusy(true)
    try {
      await markNotificationRead(id)
      setUnread((u) => Math.max(0, u - 1))
      setItems((list) => list?.map((n) => (n.id === id ? { ...n, isRead: true } : n)) ?? null)
    } finally {
      setBusy(false)
    }
  }

  async function markAll() {
    setBusy(true)
    try {
      await markAllNotificationsRead()
      setUnread(0)
      setItems((list) => list?.map((n) => ({ ...n, isRead: true })) ?? null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => void toggle()}
        aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
        className={`relative grid h-9 w-9 place-items-center rounded-xl transition duration-200 ease-soft ${
          open ? 'bg-stone-100 text-stone-900' : 'text-stone-500 hover:bg-stone-100 hover:text-stone-900'
        }`}
      >
        <BellIcon className="h-4.5 w-4.5" />
        {unread > 0 ? (
          <span className="absolute -top-0.5 -right-0.5 grid h-4 min-w-4 place-items-center rounded-full bg-brand-600 px-1 text-[10px] font-bold text-white ring-2 ring-white">
            {unread > 9 ? '9+' : unread}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="absolute right-0 z-20 mt-2 w-80 origin-top-right animate-scale-in overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-pop">
          <div className="flex items-center justify-between border-b border-stone-100 px-4 py-3">
            <p className="font-display text-sm font-semibold">Notifications</p>
            <div className="flex items-center gap-3 text-xs">
              <button
                type="button"
                className="font-semibold text-brand-700 transition hover:text-brand-800 hover:underline disabled:opacity-50"
                disabled={busy || unread === 0}
                onClick={() => void markAll()}
              >
                Mark all read
              </button>
              <Link
                to="/notifications"
                onClick={() => setOpen(false)}
                className="text-stone-500 transition hover:text-stone-800 hover:underline"
              >
                View all
              </Link>
            </div>
          </div>
          <div className="max-h-96 overflow-y-auto">
            {items === null ? (
              <div className="px-4 py-2">
                <Spinner label="Checking notifications…" />
              </div>
            ) : items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-stone-500">You’re all caught up.</p>
            ) : (
              <ul className="divide-y divide-stone-100">
                {items.map((n) => (
                  <li key={n.id}>
                    <button
                      type="button"
                      onClick={() => void openOne(n.id)}
                      className={`relative block w-full px-4 py-3 text-left transition hover:bg-stone-50 ${
                        n.isRead ? '' : 'bg-brand-50/40'
                      }`}
                    >
                      {!n.isRead ? (
                        <span className="absolute top-4 left-1.5 h-1.5 w-1.5 rounded-full bg-brand-600" />
                      ) : null}
                      <div className="flex items-center justify-between gap-2">
                        <span className="text-[11px] font-semibold tracking-wide text-brand-700 uppercase">
                          {labelOf(NOTIFICATION_TYPES, n.type)}
                        </span>
                        <span className="shrink-0 text-xs text-stone-400">{timeAgo(n.createdAtUtc)}</span>
                      </div>
                      <p className="mt-0.5 text-sm font-medium text-stone-800">{n.title}</p>
                      {n.body ? (
                        <p className="mt-0.5 line-clamp-2 text-xs text-stone-500">{n.body}</p>
                      ) : null}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      ) : null}
    </div>
  )
}