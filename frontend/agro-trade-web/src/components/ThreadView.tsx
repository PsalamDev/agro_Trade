import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from '../lib/auth-context'
import { getThread, sendMessage, markThreadRead } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc } from '../lib/format'
import { MESSAGE_CONTEXT_TYPES, labelOf } from '../types/trade'
import type { MessageDto } from '../types/trade'
import { ErrorAlert, Spinner } from './ui'

interface ThreadViewProps {
  threadKey: string | null
  /** Set when a fresh reply creates/returns a threadKey. */
  onThreadKeyChange?: (threadKey: string) => void
  /** RFQ threads: include the Rfq context so replies stay scoped. */
  contextType?: number
  contextId?: string
  /** Buyer→supplier RFQ replies require the recipient's user id. */
  recipientUserId?: string
  compact?: boolean
}

export function ThreadView({
  threadKey,
  onThreadKeyChange,
  contextType,
  contextId,
  recipientUserId,
  compact,
}: ThreadViewProps) {
  const { user } = useAuth()
  const [messages, setMessages] = useState<MessageDto[]>([])
  const [loadedThreadKey, setLoadedThreadKey] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [sending, setSending] = useState(false)
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)

  // Only show messages fetched for the thread currently selected.
  const viewMessages = loadedThreadKey === threadKey ? messages : []

  useEffect(() => {
    let cancelled = false
    if (!threadKey) return
    setLoading(true)
    setError(null)
    getThread(threadKey)
      .then(async (result) => {
        if (cancelled) return
        setMessages(result.items)
        setLoadedThreadKey(threadKey)
        if (result.items.some((m) => m.recipientUserId === user?.id && !m.isRead)) {
          try {
            await markThreadRead(threadKey)
          } catch {
            /* mark-read is best-effort */
          }
        }
      })
      .catch((err) => {
        if (!cancelled) setError(problemFrom(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [threadKey, user?.id])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: 'nearest' })
  }, [messages.length])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!draft.trim() || sending) return
    setSending(true)
    setError(null)
    try {
      const sent = await sendMessage({
        body: draft.trim(),
        contextType,
        contextId,
        recipientUserId,
      })
      setDraft('')
      setMessages((list) => [...list, sent])
      if (!threadKey && onThreadKeyChange) onThreadKeyChange(sent.threadKey)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setSending(false)
    }
  }

  return (
    <div className={compact ? '' : 'flex h-[28rem] flex-col'}>
      <div className={`flex-1 space-y-3 overflow-y-auto ${compact ? 'max-h-80' : ''} pr-1`}>
        {loading ? (
          <Spinner label="Loading messages…" />
        ) : viewMessages.length === 0 ? (
          <p className={`text-sm text-stone-500 ${compact ? 'py-2' : 'py-8 text-center'}`}>
            No messages yet in this thread.
          </p>
        ) : (
          viewMessages.map((message) => {
            const mine = message.senderUserId === user?.id
            return (
              <div key={message.id} className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
                <div
                  className={`max-w-[80%] rounded-xl px-3 py-2 text-sm shadow-sm ${
                    mine ? 'bg-brand-600 text-white' : 'border border-stone-200 bg-white text-stone-800'
                  }`}
                >
                  <p className={`mb-0.5 text-xs ${mine ? 'text-brand-100' : 'text-stone-400'}`}>
                    {message.senderDisplayName} · {labelOf(MESSAGE_CONTEXT_TYPES, message.contextType)}
                  </p>
                  <p className="whitespace-pre-wrap break-words">{message.body}</p>
                  <p className={`mt-1 text-right text-[10px] ${mine ? 'text-brand-100' : 'text-stone-400'}`}>
                    {formatUtc(message.sentAtUtc)}
                  </p>
                </div>
              </div>
            )
          })
        )}
        <div ref={bottomRef} />
      </div>

      {error ? (
        <div className="mt-3">
          <ErrorAlert detail={error.detail} />
        </div>
      ) : null}

      <form onSubmit={handleSubmit} className={`flex items-end gap-2 ${compact ? 'mt-3' : 'mt-3'}`}>
        <label htmlFor="thread-body" className="sr-only">
          Reply
        </label>
        <input
          id="thread-body"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Write a reply… (if the seller has quoted, this opens the negotiation thread)"
          className="min-w-0 flex-1 rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
        />
        <button
          type="submit"
          disabled={sending || !draft.trim()}
          className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {sending ? 'Sending…' : 'Send'}
        </button>
      </form>
    </div>
  )
}