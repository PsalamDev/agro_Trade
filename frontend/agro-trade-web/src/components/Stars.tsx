import { StarIcon } from './icons'

/** Read-only star display: filled to the nearest whole star, with an optional score and count. */
export function Stars({
  value,
  count,
  size = 'md',
  className = '',
}: {
  value: number
  count?: number
  size?: 'sm' | 'md' | 'lg'
  className?: string
}) {
  const iconClass = size === 'lg' ? 'h-5 w-5' : size === 'sm' ? 'h-3.5 w-3.5' : 'h-4 w-4'
  const rounded = Math.round(value)

  return (
    <span
      className={`inline-flex items-center gap-1.5 ${className}`}
      aria-label={`${value.toFixed(1)} out of 5`}
    >
      <span className="inline-flex">
        {[1, 2, 3, 4, 5].map((star) => (
          <StarIcon
            key={star}
            fill="currentColor"
            strokeWidth={0}
            className={`${iconClass} ${star <= rounded ? 'text-amber-500' : 'text-stone-300'}`}
          />
        ))}
      </span>
      {typeof count === 'number' ? (
        <span className="text-xs font-medium text-stone-500">
          {value.toFixed(1)} ({count})
        </span>
      ) : null}
    </span>
  )
}

/** An interactive 1–5 star picker for leaving a rating. */
export function StarRatingInput({
  value,
  onChange,
  disabled = false,
}: {
  value: number
  onChange: (value: number) => void
  disabled?: boolean
}) {
  return (
    <span className="inline-flex" role="radiogroup" aria-label="Rating">
      {[1, 2, 3, 4, 5].map((star) => (
        <button
          key={star}
          type="button"
          role="radio"
          aria-checked={value === star}
          aria-label={`${star} star${star > 1 ? 's' : ''}`}
          disabled={disabled}
          onClick={() => onChange(star)}
          className="rounded p-0.5 disabled:cursor-not-allowed"
        >
          <StarIcon
            fill="currentColor"
            strokeWidth={0}
            className={`h-7 w-7 ${star <= value ? 'text-amber-500' : 'text-stone-300 hover:text-amber-300'}`}
          />
        </button>
      ))}
    </span>
  )
}
