import type { InputHTMLAttributes, ReactNode } from 'react'

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