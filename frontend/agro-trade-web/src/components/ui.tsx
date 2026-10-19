import { useState } from 'react'
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import { AlertCircleIcon, CheckIcon, InboxIcon } from './icons'

/* -------------------------------------------------------------------------- */
/* Buttons                                                                    */
/* -------------------------------------------------------------------------- */

const buttonVariants = {
  primary: 'bg-brand-600 text-white shadow-brand hover:bg-brand-700 active:bg-brand-800',
  secondary: 'border border-stone-300 bg-white text-stone-700 shadow-soft hover:border-stone-400 hover:bg-stone-50',
  subtle: 'bg-brand-50 text-brand-700 hover:bg-brand-100',
  ghost: 'text-stone-600 hover:bg-stone-100 hover:text-stone-900',
  danger: 'bg-red-600 text-white shadow-soft hover:bg-red-700',
} as const

const buttonSizes = {
  sm: 'h-8 gap-1.5 px-3 text-xs',
  md: 'h-10 gap-2 px-4 text-sm',
  lg: 'h-12 gap-2 px-6 text-sm',
} as const

export type ButtonVariant = keyof typeof buttonVariants
export type ButtonSize = keyof typeof buttonSizes

const buttonBase =
  'inline-flex shrink-0 items-center justify-center rounded-xl font-semibold whitespace-nowrap transition duration-200 ease-soft select-none disabled:pointer-events-none disabled:opacity-55'

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
}

export function Button({ variant = 'primary', size = 'md', className = '', ...rest }: ButtonProps) {
  return <button className={`${buttonBase} ${buttonVariants[variant]} ${buttonSizes[size]} ${className}`} {...rest} />
}

export interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Required: icon-only controls must expose an accessible name. */
  label: string
  tone?: 'ghost' | 'secondary' | 'primary'
}

const iconButtonTones = {
  ghost: 'text-stone-500 hover:bg-stone-100 hover:text-stone-900',
  secondary: 'border border-stone-200 bg-white text-stone-600 shadow-soft hover:border-stone-300 hover:bg-stone-50',
  primary: 'bg-brand-600 text-white shadow-brand hover:bg-brand-700',
} as const

export function IconButton({ label, tone = 'ghost', className = '', children, ...rest }: IconButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl transition duration-200 ease-soft disabled:pointer-events-none disabled:opacity-55 ${iconButtonTones[tone]} ${className}`}
      {...rest}
    >
      {children}
    </button>
  )
}

/* -------------------------------------------------------------------------- */
/* Form controls                                                               */
/* -------------------------------------------------------------------------- */

const controlClass =
  'w-full rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

const labelClass = 'mb-1.5 block text-sm font-medium text-stone-700'

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
      className="flex gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
    >
      <AlertCircleIcon className="mt-0.5 h-4 w-4 shrink-0" />
      <div className="min-w-0">
        <p className="font-semibold">{title ?? 'Request failed'}</p>
        {detail ? <p className="mt-0.5 opacity-90">{detail}</p> : null}
        {fieldErrors ? (
          <ul className="mt-2 list-inside list-disc space-y-0.5 opacity-90">
            {Object.entries(fieldErrors).flatMap(([field, messages]) =>
              messages.map((message) => <li key={`${field}:${message}`}>{message}</li>),
            )}
          </ul>
        ) : null}
      </div>
    </div>
  )
}

export function InfoBanner({
  tone = 'info',
  title,
  children,
}: {
  tone?: 'info' | 'brand' | 'warning'
  title?: string
  children: ReactNode
}) {
  const tones = {
    info: 'border-stone-200 bg-stone-50 text-stone-700',
    brand: 'border-brand-200 bg-brand-50 text-brand-900',
    warning: 'border-amber-200 bg-amber-50 text-amber-900',
  } as const

  return (
    <div className={`rounded-xl border px-4 py-3 text-sm ${tones[tone]}`}>
      {title ? <p className="font-semibold">{title}</p> : null}
      {children ? <div className={title ? 'mt-0.5 opacity-90' : undefined}>{children}</div> : null}
    </div>
  )
}

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  /** Optional leading icon rendered inside the input (left side). */
  icon?: ReactNode
  /** Optional trailing control rendered inside the input (right side), e.g. a password reveal. */
  trailing?: ReactNode
  /** Optional control rendered at the right of the label row, e.g. a "Forgot password?" link. */
  labelAction?: ReactNode
  /** Optional helper or validation text under the control. */
  hint?: string
}

export function Field({ label, id, icon, trailing, labelAction, hint, ...rest }: FieldProps) {
  return (
    <label htmlFor={id} className="block">
      <span className="mb-1.5 flex items-center justify-between gap-3">
        <span className="text-sm font-medium text-stone-700">{label}</span>
        {labelAction}
      </span>
      <div className="relative">
        {icon ? (
          <span className="pointer-events-none absolute inset-y-0 left-3.5 flex items-center text-stone-400 transition peer-focus:text-brand-500">
            {icon}
          </span>
        ) : null}
        <input
          id={id}
          className={`${controlClass} peer ${icon ? 'pl-10' : ''} ${trailing ? 'pr-11' : ''}`}
          {...rest}
        />
        {trailing ? (
          <span className="absolute inset-y-0 right-1.5 flex items-center">{trailing}</span>
        ) : null}
      </div>
      {hint ? <span className="mt-1 block text-xs text-stone-500">{hint}</span> : null}
    </label>
  )
}

/** Server password policy, mirrored on the client for instant feedback. */
export const PASSWORD_RULES: { label: string; test: (value: string) => boolean }[] = [
  { label: 'At least 8 characters', test: (value) => value.length >= 8 },
  { label: 'An uppercase letter', test: (value) => /[A-Z]/.test(value) },
  { label: 'A lowercase letter', test: (value) => /[a-z]/.test(value) },
  { label: 'A number', test: (value) => /\d/.test(value) },
]

/**
 * Live password checklist shown under a password field. Falls back to a plain
 * hint until the user starts typing, so the rules are always discoverable.
 */
export function PasswordRules({ value }: { value: string }) {
  if (!value) {
    return (
      <p className="mt-1.5 text-xs text-stone-500">
        At least 8 characters, with an uppercase letter, a lowercase letter and a number.
      </p>
    )
  }

  return (
    <ul className="mt-2.5 grid gap-1.5 sm:grid-cols-2">
      {PASSWORD_RULES.map((rule) => {
        const met = rule.test(value)
        return (
          <li
            key={rule.label}
            className={`flex items-center gap-2 text-xs transition duration-200 ease-soft ${
              met ? 'text-brand-700' : 'text-stone-500'
            }`}
          >
            <span
              className={`grid h-4 w-4 shrink-0 place-items-center rounded-full ${
                met ? 'bg-brand-100 text-brand-700' : 'bg-stone-100 text-stone-400'
              }`}
            >
              <CheckIcon className="h-3 w-3" />
            </span>
            {rule.label}
          </li>
        )
      })}
    </ul>
  )
}

export function SubmitButton({
  children,
  pending,
  ...rest
}: { children: ReactNode; pending?: boolean } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      disabled={pending || rest.disabled}
      className={`${buttonBase} ${buttonVariants.primary} ${buttonSizes.lg} w-full`}
      {...rest}
    >
      {pending ? (
        <>
          <SpinnerIcon className="h-4 w-4" />
          Please wait…
        </>
      ) : (
        children
      )}
    </button>
  )
}

interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string
  options: readonly string[]
  /** When set, option values are these (parallel to options); otherwise index-based. */
  values?: readonly (string | number)[]
}

export function SelectField({ label, id, options, values, ...rest }: SelectFieldProps) {
  return (
    <label htmlFor={id} className="block">
      <span className={labelClass}>{label}</span>
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
  /** Optional leading icon rendered inside the textarea (top-left). */
  icon?: ReactNode
}

export function TextArea({ label, id, icon, ...rest }: TextAreaProps) {
  return (
    <label htmlFor={id} className="block">
      <span className={labelClass}>{label}</span>
      <div className="relative">
        {icon ? (
          <span className="pointer-events-none absolute left-3.5 top-3.5 flex items-center text-stone-400">
            {icon}
          </span>
        ) : null}
        <textarea id={id} className={`${controlClass} min-h-28 resize-y ${icon ? 'pl-10' : ''}`} {...rest} />
      </div>
    </label>
  )
}

/* -------------------------------------------------------------------------- */
/* Composite inputs                                                            */
/* -------------------------------------------------------------------------- */

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
      <span className={labelClass}>{label}</span>
      <div className="flex flex-wrap items-center gap-1.5 rounded-xl border border-stone-300 bg-white px-2.5 py-2 shadow-soft transition duration-200 ease-soft focus-within:border-brand-500 focus-within:ring-4 focus-within:ring-brand-500/15">
        {value.map((tag) => (
          <span
            key={tag}
            className="inline-flex items-center gap-1 rounded-full bg-brand-50 py-0.5 pl-2.5 pr-1.5 text-xs font-medium text-brand-800"
          >
            {tag}
            <button
              type="button"
              aria-label={`Remove ${tag}`}
              onClick={() => onChange(value.filter((t) => t !== tag))}
              className="grid h-4 w-4 place-items-center rounded-full text-brand-600 transition hover:bg-brand-200 hover:text-brand-900"
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
          className="min-w-40 flex-1 border-none bg-transparent py-0.5 text-sm outline-none placeholder:text-stone-400"
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
      <span className={labelClass}>{label}</span>
      <div className="space-y-2">
        {entries.length > 0 ? (
          <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200 bg-white">
            {entries.map(([k, v]) => (
              <li key={k} className="flex items-start justify-between gap-3 px-3.5 py-2.5 text-sm">
                <span className="font-medium text-stone-700">{k}</span>
                <span className="min-w-0 flex-1 text-right text-stone-600">{v}</span>
                <button
                  type="button"
                  aria-label={`Remove ${k}`}
                  onClick={() => {
                    const next = { ...value }
                    delete next[k]
                    onChange(next)
                  }}
                  className="text-stone-400 transition hover:text-red-600"
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="rounded-xl border border-dashed border-stone-300 px-3.5 py-4 text-sm text-stone-400">
            No entries yet.
          </p>
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
          <Button type="button" variant="secondary" onClick={commit} className="h-auto px-3.5">
            Add
          </Button>
        </div>
      </div>
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Feedback + layout primitives                                                */
/* -------------------------------------------------------------------------- */

export function SpinnerIcon({ className = 'h-4 w-4' }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" className={`animate-spin ${className}`}>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="2.5" />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
      />
    </svg>
  )
}

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex items-center gap-2.5 py-10 text-sm text-stone-500">
      <SpinnerIcon className="h-4 w-4 text-brand-600" />
      {label}
    </div>
  )
}

/** Grey placeholder block used to build content-shaped loading states. */
export function Skeleton({ className = '' }: { className?: string }) {
  return <div className={`animate-shimmer rounded-lg bg-stone-200/70 ${className}`} />
}

/** Grid of card-shaped skeletons, matching the marketplace / listing layouts. */
export function SkeletonGrid({ count = 6, className = '' }: { count?: number; className?: string }) {
  return (
    <div className={`grid gap-5 sm:grid-cols-2 lg:grid-cols-3 ${className}`}>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="rounded-2xl border border-stone-200 bg-white p-4 shadow-soft">
          <Skeleton className="h-36 w-full rounded-xl" />
          <Skeleton className="mt-4 h-4 w-3/4" />
          <Skeleton className="mt-3 h-3 w-1/2" />
          <div className="mt-4 grid grid-cols-2 gap-3">
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
          </div>
        </div>
      ))}
    </div>
  )
}

export function EmptyState({
  title,
  body,
  icon,
  action,
}: {
  title: string
  body?: string
  icon?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="rounded-2xl border border-dashed border-stone-300 bg-white/60 px-6 py-14 text-center">
      <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-stone-100 text-stone-400">
        {icon ?? <InboxIcon className="h-5 w-5" />}
      </span>
      <p className="mt-4 font-display text-base font-semibold text-stone-800">{title}</p>
      {body ? <p className="mx-auto mt-1 max-w-md text-sm text-stone-500">{body}</p> : null}
      {action ? <div className="mt-5 flex justify-center">{action}</div> : null}
    </div>
  )
}

export function Eyebrow({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold tracking-wide text-brand-800 uppercase ${className}`}
    >
      {children}
    </span>
  )
}

export function PageHeader({
  title,
  subtitle,
  eyebrow,
  action,
}: {
  title: string
  subtitle?: string
  eyebrow?: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        {eyebrow ? (
          <p className="mb-2 text-xs font-semibold tracking-widest text-brand-600 uppercase">{eyebrow}</p>
        ) : null}
        <h1 className="font-display text-2xl font-bold tracking-tight sm:text-3xl">{title}</h1>
        {subtitle ? <p className="mt-1.5 max-w-2xl text-sm text-stone-500">{subtitle}</p> : null}
      </div>
      {action ? <div className="flex shrink-0 items-center gap-2">{action}</div> : null}
    </div>
  )
}

/** Wraps content in a white card with border + soft shadow. */
export function Card({
  children,
  className = '',
  hover = false,
  as: Tag = 'div',
}: {
  children: ReactNode
  className?: string
  hover?: boolean
  as?: 'div' | 'section' | 'article' | 'li'
}) {
  return (
    <Tag
      className={`rounded-2xl border border-stone-200 bg-white p-5 shadow-card transition duration-300 ease-soft ${
        hover ? 'hover:-translate-y-0.5 hover:border-brand-200 hover:shadow-lift' : ''
      } ${className}`}
    >
      {children}
    </Tag>
  )
}

/** Small labelled metric used on dashboards and summary strips. */
export function Stat({
  label,
  value,
  icon,
  tone = 'brand',
}: {
  label: string
  value: ReactNode
  icon?: ReactNode
  tone?: 'brand' | 'neutral' | 'amber'
}) {
  const tones = {
    brand: 'bg-brand-50 text-brand-700',
    neutral: 'bg-stone-100 text-stone-600',
    amber: 'bg-amber-50 text-amber-700',
  } as const

  return (
    <div className="flex items-center gap-3 rounded-2xl border border-stone-200 bg-white px-4 py-3.5 shadow-soft">
      {icon ? (
        <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${tones[tone]}`}>{icon}</span>
      ) : null}
      <div className="min-w-0">
        <p className="truncate text-xs font-medium tracking-wide text-stone-500 uppercase">{label}</p>
        <p className="font-display text-lg font-bold text-stone-900">{value}</p>
      </div>
    </div>
  )
}
