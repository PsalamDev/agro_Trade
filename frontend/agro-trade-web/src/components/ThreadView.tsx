import { Fragment, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from '../lib/auth-context'
import { getThread, sendMessage, markThreadRead } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc, formatUtcDayLabel, formatUtcTime } from '../lib/format'
import { MESSAGE_CONTEXT_TYPES, labelOf } from '../types/trade'
import type { MessageDto } from '../types/trade'
import { Button, ErrorAlert, Skeleton } from './ui'
import { MessageIcon } from './icons'

interface ThreadViewProps {
  threadKey: string | null
  /** Set when a fresh reply creates/returns a threadKey. */
  onThreadKeyChange?: (threadKey: string) => void
  /** RFQ threads: include the Rfq context so replies stay scoped. */
  contextType?: number
  contextId?: string
  /** Buyer→supplier RFQ replies require the recipient's user id. */
  recipientUserId?: string
  /**
   * Outer height. Defaults to a fixed box so the view can sit inside a card; pass `h-full` when the
   * parent already constrains the height and the thread should fill it.
   */
  className?: string
}

function DaySeparator({ label }: { label: string }) {
  return (
    <div className="my-3 flex items-center gap-3 first:mt-0">
      <span className="h-px flex-1 bg-stone-100" />
      <span className="text-[11px] font-medium tracking-wide text-stone-400 uppercase">{label}</span>
      <span className="h-px flex-1 bg-stone-100" />
    </div>
  )
}

export function ThreadView({
  threadKey,
  onThreadKeyChange,
  contextType,
  contextId,
  recipientUserId,
  className,
}: ThreadViewProps) {
  const { user } = useAuth()
  const [messages, setMessages] = useState<MessageDto[]>([])
  const [loadedThreadKey, setLoadedThreadKey] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [sending, setSending] = useState(false)
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)
  const listRef = useRef<HTMLDivElement>(null)

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

  // Pin to the newest message. Scrolling the container directly keeps the page still, which
  // `scrollIntoView` on a nested box would not.
  useEffect(() => {
    const list = listRef.current
    if (list) list.scrollTop = list.scrollHeight
  }, [viewMessages.length, threadKey])

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
    <div className={`flex flex-col ${className ?? 'h-[28rem]'}`}>
      <div ref={listRef} className="flex-1 overflow-y-auto pr-1">
        {loading ? (
          <div className="space-y-4 py-2" aria-hidden>
            <Skeleton className="h-10 w-2/3" />
            <Skeleton className="ml-auto h-10 w-1/2" />
            <Skeleton className="h-10 w-3/5" />
          </div>
        ) : viewMessages.length === 0 ? (
          <div className="grid h-full place-items-center px-4 py-8 text-center">
            <div>
              <span className="mx-auto grid h-11 w-11 place-items-center rounded-2xl bg-stone-100 text-stone-400">
                <MessageIcon className="h-5 w-5" />
              </span>
              <p className="mt-3 font-display text-sm font-semibold text-stone-700">No messages yet</p>
              <p className="mt-1 text-sm text-stone-500">Send the first message to start the conversation.</p>
            </div>
          </div>
        ) : (
          viewMessages.map((message, index) => {
            const mine = message.senderUserId === user?.id
            const previous = viewMessages[index - 1]
            const showDay =
              index === 0 || formatUtcDayLabel(previous.sentAtUtc) !== formatUtcDayLabel(message.sentAtUtc)
            const grouped = !showDay && previous.senderUserId === message.senderUserId
            const showName = !mine && !grouped

            return (
              <Fragment key={message.id}>
                {showDay ? <DaySeparator label={formatUtcDayLabel(message.sentAtUtc)} /> : null}
                <div
                  className={`flex ${mine ? 'justify-end' : 'justify-start'} ${
                    showDay ? 'mt-1' : grouped ? 'mt-0.5' : 'mt-3'
                  }`}
                >
                  <div
                    className={`max-w-[85%] rounded-2xl px-3.5 py-2 text-sm shadow-sm ${
                      mine
                        ? 'rounded-br-md bg-brand-600 text-white'
                        : 'rounded-bl-md border border-stone-200 bg-white text-stone-800'
                    }`}
                  >
                    {showName ? (
                      <p className="mb-0.5 text-xs font-semibold text-stone-500">{message.senderDisplayName}</p>
                    ) : null}
                    <p className="whitespace-pre-wrap break-words">{message.body}</p>
                    <p
                      className={`mt-1 text-right text-[10px] ${mine ? 'text-brand-100' : 'text-stone-400'}`}
                      title={`${formatUtc(message.sentAtUtc)} · ${labelOf(MESSAGE_CONTEXT_TYPES, message.contextType)}`}
                    >
                      {formatUtcTime(message.sentAtUtc)}
                    </p>
                  </div>
                </div>
              </Fragment>
            )
          })
        )}
      </div>

      {error ? (
        <div className="mt-3">
          <ErrorAlert detail={error.detail} />
        </div>
      ) : null}

      <form onSubmit={handleSubmit} className="mt-3 flex items-end gap-2 border-t border-stone-100 pt-3">
        <label htmlFor="thread-body" className="sr-only">
          Reply
        </label>
        <input
          id="thread-body"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Write a message…"
          className="min-w-0 flex-1 rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft outline-none transition duration-200 ease-soft placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15"
        />
        <Button type="submit" disabled={sending || !draft.trim()}>
          {sending ? 'Sending…' : 'Send'}
        </Button>
      </form>
    </div>
  )
}
