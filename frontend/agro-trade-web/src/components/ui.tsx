import { useState } from 'react'
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'

interface ErrorAlertProps {
  title?: string
  detail?: string
  fieldErrors?: Record<string, string[]>
}

export function ErrorAlert({ title, detail, fieldErrors }: ErrorAlertProps) {
  if (!title && !detail && !fieldErrors) return null

  return (
    <div
      role="alert"
      className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
    >
      <p className="font-medium">{title ?? 'Request failed'}</p>
      {detail ? <p className="mt-0.5">{detail}</p> : null}
      {fieldErrors ? (
        <ul className="mt-2 list-inside list-disc space-y-0.5">
          {Object.entries(fieldErrors).flatMap(([field, messages]) =>
            messages.map((message) => <li key={`${field}:${message}`}>{message}</li>),
          )}
        </ul>
      ) : null}
    </div>
  )
}

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
}

export function Field({ label, id, ...rest }: FieldProps) {
  return (
    <label htmlFor={id} className="block">
      <span className="mb-1 block text-sm font-medium text-stone-700">{label}</span>
      <input
        id={id}
        className="w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
        {...rest}
      />
    </label>
  )
}

export function SubmitButton({
  children,
  pending,
  ...rest
}: { children: ReactNode; pending?: boolean } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      disabled={pending}
      className="w-full rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-brand-700 focus:outline-none focus:ring-2 focus:ring-brand-500/40 disabled:cursor-not-allowed disabled:opacity-60"
      {...rest}
    >
      {pending ? 'Please wait…' : children}
    </button>
  )
}

const controlClass =
  'w-full rounded-lg border border-stone-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 disabled:bg-stone-100'

interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string
  options: readonly string[]
  /** When set, option values are these (parallel to options); otherwise index-based. */
  values?: readonly (string | number)[]
}

export function SelectField({ label, id, options, values, ...rest }: SelectFieldProps) {
  return (
    <label htmlFor={id} className="block">
      <span className="mb-1 block text-sm font-medium text-stone-700">{label}</span>
      <select id={id} className={controlClass} {...rest}>
        <option value="">— Select —</option>
        {options.map((option, index) => (
          <option key={option} value={values ? String(values[index]) : String(index)}>
            {option}
          </option>
        ))}
      </select>
    </label>
  )
}

interface TextAreaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string
}

export function TextArea({ label, id, ...rest }: TextAreaProps) {
  return (
    <label htmlFor={id} className="block">
      <span className="mb-1 block text-sm font-medium text-stone-700">{label}</span>
      <textarea id={id} className={`${controlClass} min-h-24 resize-y`} {...rest} />
    </label>
  )
}

/** Comma-separated tag list input (tags displayed as pills, removed on click). */
export function TagInput({
  label,
  id,
  value,
  onChange,
}: {
  label: string
  id: string
  value: string[]
  onChange: (tags: string[]) => void
}) {
  const [draft, setDraft] = useState('')

  function commit() {
    const trimmed = draft.trim()
    if (trimmed && !value.some((t) => t.toLowerCase() === trimmed.toLowerCase())) {
      onChange([...value, trimmed])
    }
    setDraft('')
  }

  return (
    <div className="block">
      <span className="mb-1 block text-sm font-medium text-stone-700">{label}</span>
      <div className="flex flex-wrap items-center gap-1.5 rounded-lg border border-stone-300 bg-white px-2 py-1.5 shadow-sm focus-within:border-brand-500 focus-within:ring-2 focus-within:ring-brand-500/30">
        {value.map((tag) => (
          <span
            key={tag}
            className="inline-flex items-center gap-1 rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-800"
          >
            {tag}
            <button
              type="button"
              aria-label={`Remove ${tag}`}
              onClick={() => onChange(value.filter((t) => t !== tag))}
              className="text-brand-600 hover:text-brand-900"
            >
              ×
            </button>
          </span>
        ))}
        <input
          id={id}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ',') {
              e.preventDefault()
              commit()
            }
            if (e.key === 'Backspace' && !draft && value.length > 0) {
              onChange(value.slice(0, -1))
            }
          }}
          onBlur={commit}
          placeholder="Type and press Enter / comma"
          className="min-w-40 flex-1 border-none bg-transparent py-1 text-sm outline-none placeholder:text-stone-400"
        />
      </div>
    </div>
  )
}

/** Simple key/value pair editor for quality specs / target specs. */
export function KeyValueEditor({
  label,
  id,
  value,
  onChange,
  placeholder,
}: {
  label: string
  id: string
  value: Record<string, string>
  onChange: (next: Record<string, string>) => void
  placeholder?: string
}) {
  const [key, setKey] = useState('')
  const [val, setVal] = useState('')
  const entries = Object.entries(value)

  function commit() {
    const trimmed = key.trim()
    if (trimmed && val.trim()) {
      onChange({ ...value, [trimmed]: val.trim() })
      setKey('')
      setVal('')
    }
  }

  return (
    <div className="block">
      <span className="mb-1 block text-sm font-medium text-stone-700">{label}</span>
      <div className="space-y-2">
        {entries.length > 0 ? (
          <ul className="divide-y divide-stone-100 rounded-lg border border-stone-200">
            {entries.map(([k, v]) => (
              <li key={k} className="flex items-start justify-between gap-3 px-3 py-2 text-sm">
                <span className="font-medium text-stone-700">{k}</span>
                <span className="flex items-center gap-2 text-stone-600">{v}</span>
                <button
                  type="button"
                  aria-label={`Remove ${k}`}
                  onClick={() => {
                    const next = { ...value }
                    delete next[k]
                    onChange(next)
                  }}
                  className="text-stone-400 hover:text-red-600"
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-stone-400">No entries yet.</p>
        )}
        <div className="flex gap-2">
          <input
            id={`${id}-key`}
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder={placeholder ?? 'Name (e.g. moisture)'}
            className={`${controlClass} flex-1`}
          />
          <input
            id={`${id}-value`}
            value={val}
            onChange={(e) => setVal(e.target.value)}
            placeholder="Value (e.g. 10.5% max)"
            className={`${controlClass} flex-1`}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                commit()
              }
            }}
          />
          <button
            type="button"
            onClick={commit}
            className="rounded-lg border border-stone-300 bg-stone-50 px-3 py-2 text-sm font-medium text-stone-700 transition hover:bg-stone-100"
          >
            Add
          </button>
        </div>
      </div>
    </div>
  )
}

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return <p className="animate-pulse text-sm text-stone-500">{label}</p>
}

export function EmptyState({ title, body }: { title: string; body?: string }) {
  return (
    <div className="rounded-xl border border-dashed border-stone-300 bg-stone-50 px-6 py-12 text-center">
      <p className="font-medium text-stone-700">{title}</p>
      {body ? <p className="mt-1 text-sm text-stone-500">{body}</p> : null}
    </div>
  )
}

export function PageHeader({
  title,
  subtitle,
  action,
}: {
  title: string
  subtitle?: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-stone-500">{subtitle}</p> : null}
      </div>
      {action}
    </div>
  )
}

/** Wraps content in a white card with border + shadow, matching dashboard tiles. */
export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-xl border border-stone-200 bg-white p-5 shadow-sm ${className}`}>{children}</div>
}