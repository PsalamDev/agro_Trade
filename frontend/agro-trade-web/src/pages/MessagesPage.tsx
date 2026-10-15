import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ThreadView } from '../components/ThreadView'
import { EmptyState, ErrorAlert, PageHeader, Skeleton } from '../components/ui'
import { getConversations } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtcDate, formatUtcDayLabel, formatUtcTime } from '../lib/format'
import { MESSAGE_CONTEXT_TYPES, MESSAGE_CONTEXT_TYPE, labelOf } from '../types/trade'
import type { ConversationDto } from '../types/trade'
import { MessageIcon, SearchIcon } from '../components/icons'

/** Where a thread's context lives, so a thread can link back to the order or RFQ it belongs to. */
const CONTEXT_META: Partial<Record<number, { href: (id: string) => string; noun: string }>> = {
  [MESSAGE_CONTEXT_TYPE.Order]: { href: (id) => `/dashboard/orders/${id}`, noun: 'order' },
  [MESSAGE_CONTEXT_TYPE.Rfq]: { href: (id) => `/rfqs/${id}`, noun: 'RFQ' },
}

function initials(name: string) {
  const [first, second] = name.trim().split(/\s+/)
  return `${first?.[0] ?? ''}${second?.[0] ?? ''}`.toUpperCase() || '?'
}

/** "RFQ thread", "Order thread" or "Direct message" — General is not a thread anyone chose. */
function contextLabel(type: number) {
  const label = labelOf(MESSAGE_CONTEXT_TYPES, type)
  return label === 'General' ? 'Direct message' : `${label} thread`
}

/** Short tag for the list row, where the full phrase is too long for the pill. */
function contextTag(type: number) {
  const label = labelOf(MESSAGE_CONTEXT_TYPES, type)
  return label === 'General' ? 'Direct' : label
}

/** Compact list timestamp: the clock time for today, the day label otherwise, both in UTC. */
function listStamp(iso: string) {
  const label = formatUtcDayLabel(iso)
  return label === 'Today' ? formatUtcTime(iso) : label
}

function Avatar({ name, className = '' }: { name: string; className?: string }) {
  return (
    <span
      aria-hidden
      className={`grid shrink-0 place-items-center rounded-full bg-brand-100 font-display text-sm font-semibold text-brand-700 ${className}`}
    >
      {initials(name)}
    </span>
  )
}

function ConversationSkeleton() {
  return (
    <li className="flex items-start gap-3 rounded-xl border border-stone-200 bg-white p-3.5">
      <Skeleton className="h-10 w-10 rounded-full" />
      <div className="min-w-0 flex-1">
        <Skeleton className="h-3.5 w-1/2" />
        <Skeleton className="mt-2 h-3 w-3/4" />
      </div>
    </li>
  )
}

export default function MessagesPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const threadKeyParam = searchParams.get('thread') ?? ''
  const [conversations, setConversations] = useState<ConversationDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [selected, setSelected] = useState<string>(threadKeyParam)
  const [query, setQuery] = useState('')

  const load = useCallback(async () => {
    setError(null)
    try {
      const result = await getConversations()
      setConversations(result.items)
    } catch (err) {
      setError(problemFrom(err))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    setSelected(threadKeyParam)
  }, [threadKeyParam])

  const active = conversations?.find((c) => c.threadKey === selected) ?? null

  const filtered = useMemo(() => {
    if (!conversations) return null
    const needle = query.trim().toLowerCase()
    if (!needle) return conversations
    return conversations.filter(
      (c) =>
        c.otherDisplayName.toLowerCase().includes(needle) ||
        c.lastMessage.toLowerCase().includes(needle),
    )
  }, [conversations, query])

  const meta = active?.contextId ? CONTEXT_META[active.contextType] : undefined

  return (
    <div className="flex min-h-screen flex-col">
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-10">
        <PageHeader
          eyebrow="Inbox"
          title="Messages"
          subtitle="Direct messages and RFQ negotiation threads. Open a thread to reply."
        />

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load conversations" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6 grid gap-6 lg:h-[calc(100vh-15rem)] lg:min-h-[30rem] lg:grid-cols-5">
          <div className="flex min-h-0 flex-col lg:col-span-2">
            <div className="relative">
              <span className="pointer-events-none absolute inset-y-0 left-3.5 flex items-center text-stone-400">
                <SearchIcon className="h-4 w-4" />
              </span>
              <input
                type="search"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search conversations"
                aria-label="Search conversations"
                className="w-full rounded-xl border border-stone-300 bg-white py-2.5 pr-3.5 pl-10 text-sm shadow-soft outline-none transition duration-200 ease-soft placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
              />
            </div>

            <div className="mt-3 min-h-0 flex-1 lg:overflow-y-auto lg:pr-1">
              {filtered === null ? (
                <ul aria-busy="true" aria-label="Loading conversations" className="space-y-2">
                  {Array.from({ length: 5 }, (_, i) => (
                    <ConversationSkeleton key={i} />
                  ))}
                </ul>
              ) : conversations && conversations.length === 0 ? (
                <EmptyState
                  title="No conversations yet"
                  body="Contact a supplier from an RFQ, or send a message from the RFQ detail page."
                />
              ) : filtered.length === 0 ? (
                <p className="rounded-xl border border-dashed border-stone-300 px-4 py-8 text-center text-sm text-stone-500">
                  No conversations match “{query.trim()}”.
                </p>
              ) : (
                <ul className="space-y-2">
                  {filtered.map((conversation) => {
                    const isSelected = selected === conversation.threadKey
                    const unread = conversation.unreadCount > 0
                    return (
                      <li key={conversation.threadKey}>
                        <button
                          type="button"
                          aria-current={isSelected ? 'true' : undefined}
                          onClick={() => {
                            setSelected(conversation.threadKey)
                            setSearchParams({ thread: conversation.threadKey })
                          }}
                          className={`flex w-full items-start gap-3 rounded-xl border p-3.5 text-left transition duration-200 ease-soft ${
                            isSelected
                              ? 'border-brand-300 bg-brand-50/60'
                              : 'border-stone-200 bg-white hover:border-brand-200 hover:bg-stone-50'
                          }`}
                        >
                          <Avatar name={conversation.otherDisplayName} className="h-10 w-10" />
                          <div className="min-w-0 flex-1">
                            <div className="flex items-baseline justify-between gap-2">
                              <p
                                className={`truncate ${
                                  unread ? 'font-semibold text-stone-900' : 'font-medium text-stone-800'
                                }`}
                              >
                                {conversation.otherDisplayName}
                              </p>
                              <span className="shrink-0 text-[11px] text-stone-400">
                                {listStamp(conversation.lastMessageAtUtc)}
                              </span>
                            </div>
                            <span className="mt-1 inline-block rounded-full bg-stone-100 px-1.5 py-0.5 text-[10px] font-semibold tracking-wide text-stone-500 uppercase">
                              {contextTag(conversation.contextType)}
                            </span>
                            <div className="mt-1 flex items-center gap-2">
                              <p
                                className={`min-w-0 flex-1 truncate text-sm ${
                                  unread ? 'font-medium text-stone-700' : 'text-stone-500'
                                }`}
                              >
                                {conversation.lastMessage}
                              </p>
                              {unread ? (
                                <span className="shrink-0 rounded-full bg-brand-600 px-1.5 py-0.5 text-[10px] font-bold text-white">
                                  {conversation.unreadCount}
                                </span>
                              ) : null}
                            </div>
                          </div>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              )}
            </div>
          </div>

          <div className="lg:col-span-3 lg:min-h-0">
            <div className="flex h-full min-h-[32rem] flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-card">
              {active ? (
                <>
                  <header className="flex items-center gap-3 border-b border-stone-100 px-5 py-4">
                    <Avatar name={active.otherDisplayName} className="h-11 w-11" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-semibold text-stone-900">{active.otherDisplayName}</p>
                      <p className="truncate text-xs text-stone-500">
                        {contextLabel(active.contextType)} · updated {formatUtcDate(active.lastMessageAtUtc)}
                      </p>
                    </div>
                    {meta ? (
                      <Link
                        to={meta.href(active.contextId ?? '')}
                        className="hidden shrink-0 rounded-lg border border-stone-200 bg-white px-3 py-1.5 text-xs font-semibold text-stone-700 shadow-soft transition hover:border-stone-300 hover:bg-stone-50 sm:inline-flex"
                      >
                        View {meta.noun}
                      </Link>
                    ) : null}
                  </header>

                  <div className="flex min-h-0 flex-1 flex-col px-5 pt-4 pb-5">
                    <ThreadView
                      threadKey={active.threadKey}
                      contextType={active.contextType}
                      contextId={active.contextId ?? undefined}
                      recipientUserId={active.otherUserId}
                      className="h-full"
                    />
                  </div>
                </>
              ) : (
                <div className="grid flex-1 place-items-center p-8">
                  <div className="text-center">
                    <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-stone-100 text-stone-400">
                      <MessageIcon className="h-5 w-5" />
                    </span>
                    <p className="mt-4 font-display font-semibold text-stone-800">Select a conversation</p>
                    <p className="mt-1 text-sm text-stone-500">
                      Threads from RFQ negotiations and direct messages appear here.
                    </p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      </main>
    </div>
  )
}
