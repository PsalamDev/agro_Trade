import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { getNotifications, getUnreadNotificationCount, markAllNotificationsRead, markNotificationRead } from '../lib/trade'
import { NOTIFICATION_TYPES, labelOf } from '../types/trade'
import type { NotificationDto } from '../types/trade'
import { Spinner } from './ui'

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
        aria-label="Notifications"
        className="relative rounded-md px-2.5 py-2 text-stone-600 transition hover:bg-stone-100 hover:text-stone-900"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-5 w-5" aria-hidden="true">
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
          />
        </svg>
        {unread > 0 ? (
          <span className="absolute -right-0.5 -top-0.5 grid h-4 min-w-4 place-items-center rounded-full bg-brand-600 px-1 text-[10px] font-bold text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="absolute right-0 z-20 mt-2 w-80 overflow-hidden rounded-xl border border-stone-200 bg-white shadow-lg">
          <div className="flex items-center justify-between border-b border-stone-100 px-4 py-2.5">
            <p className="text-sm font-semibold">Notifications</p>
            <div className="flex items-center gap-3 text-xs">
              <button
                type="button"
                className="text-brand-700 hover:underline disabled:opacity-50"
                disabled={busy || unread === 0}
                onClick={() => void markAll()}
              >
                Mark all read
              </button>
              <Link to="/notifications" onClick={() => setOpen(false)} className="text-stone-500 hover:underline">
                View all
              </Link>
            </div>
          </div>
          <div className="max-h-96 overflow-y-auto">
            {items === null ? (
              <div className="px-4 py-6">
                <Spinner label="Checking notifications…" />
              </div>
            ) : items.length === 0 ? (
              <p className="px-4 py-6 text-sm text-stone-500">You’re all caught up.</p>
            ) : (
              <ul className="divide-y divide-stone-100">
                {items.map((n) => (
                  <li key={n.id}>
                    <button
                      type="button"
                      onClick={() => void openOne(n.id)}
                      className={`block w-full px-4 py-3 text-left transition hover:bg-stone-50 ${n.isRead ? '' : 'bg-brand-50/40'}`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="text-xs font-semibold uppercase tracking-wide text-brand-700">
                          {labelOf(NOTIFICATION_TYPES, n.type)}
                        </span>
                        <span className="shrink-0 text-xs text-stone-400">{timeAgo(n.createdAtUtc)}</span>
                      </div>
                      <p className="mt-0.5 text-sm font-medium text-stone-800">{n.title}</p>
                      {n.body ? <p className="mt-0.5 text-xs text-stone-500 line-clamp-2">{n.body}</p> : null}
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