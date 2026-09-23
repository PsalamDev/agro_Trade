import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { ThreadView } from '../components/ThreadView'
import { Card, EmptyState, ErrorAlert, Spinner } from '../components/ui'
import { getConversations } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { MESSAGE_CONTEXT_TYPES, labelOf } from '../types/trade'
import type { ConversationDto } from '../types/trade'

export default function MessagesPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const threadKeyParam = searchParams.get('thread') ?? ''
  const [conversations, setConversations] = useState<ConversationDto[] | null>(null)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const [selected, setSelected] = useState<string>(threadKeyParam)

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

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-6xl px-4 py-10">
        <h1 className="text-2xl font-bold tracking-tight">Messages</h1>
        <p className="mt-1 text-sm text-stone-500">
          Direct messages and RFQ negotiation threads. Open a thread to reply.
        </p>

        {error ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load conversations" detail={error.detail} />
          </div>
        ) : null}

        <div className="mt-6 grid gap-6 lg:grid-cols-5">
          <div className="lg:col-span-2">
            {conversations === null ? (
              <Spinner />
            ) : conversations.length === 0 ? (
              <EmptyState title="No conversations yet" body="Contact a supplier from an RFQ, or send a message from the RFQ detail page." />
            ) : (
              <ul className="space-y-2">
                {conversations.map((conversation) => (
                  <li key={conversation.threadKey}>
                    <button
                      type="button"
                      onClick={() => {
                        setSelected(conversation.threadKey)
                        setSearchParams({ thread: conversation.threadKey })
                      }}
                      className={`w-full rounded-xl border p-4 text-left transition ${
                        selected === conversation.threadKey
                          ? 'border-brand-300 bg-brand-50/50'
                          : 'border-stone-200 bg-white hover:border-brand-200'
                      }`}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <p className="font-semibold">{conversation.otherDisplayName}</p>
                        {conversation.unreadCount > 0 ? (
                          <span className="rounded-full bg-brand-600 px-2 py-0.5 text-xs font-bold text-white">
                            {conversation.unreadCount}
                          </span>
                        ) : null}
                      </div>
                      <p className="mt-0.5 text-xs text-stone-500">
                        {labelOf(MESSAGE_CONTEXT_TYPES, conversation.contextType)}
                        {conversation.contextId ? ' · RFQ thread' : ''} · {new Date(conversation.lastMessageAtUtc).toLocaleString()}
                      </p>
                      <p className="mt-1 truncate text-sm text-stone-700">{conversation.lastMessage}</p>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="lg:col-span-3">
            <Card className="flex h-full flex-col">
              {active ? (
                <>
                  <div className="border-b border-stone-100 pb-3">
                    <p className="font-semibold">{active.otherDisplayName}</p>
                    <p className="text-xs text-stone-500">
                      {labelOf(MESSAGE_CONTEXT_TYPES, active.contextType)}
                      {active.contextId ? ` thread · ${new Date(active.lastMessageAtUtc).toLocaleDateString()}` : ''}
                    </p>
                  </div>
                  <div className="flex-1 pt-4">
                    <ThreadView
                      threadKey={active.threadKey}
                      contextType={active.contextType}
                      contextId={active.contextId ?? undefined}
                      recipientUserId={active.otherUserId}
                    />
                  </div>
                </>
              ) : (
                <div className="grid flex-1 place-items-center">
                  <div className="text-center">
                    <p className="font-medium text-stone-700">Select a conversation</p>
                    <p className="mt-1 text-sm text-stone-500">Threads from RFQ negotiations and direct messages appear here.</p>
                  </div>
                </div>
              )}
            </Card>
          </div>
        </div>
      </main>
    </div>
  )
}