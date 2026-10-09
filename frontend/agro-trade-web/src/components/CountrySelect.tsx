import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import { getCountries } from '../lib/trade'
import type { CountryDto } from '../types/trade'
import { CheckIcon, ChevronDownIcon, SearchIcon, XIcon } from './icons'

const controlClass =
  'w-full rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

const labelClass = 'mb-1.5 block text-sm font-medium text-stone-700'

interface CountrySelectProps {
  id: string
  label: string
  /** ISO-3166-1 alpha-2 code, e.g. "NG". */
  value: string
  onChange: (code: string) => void
  placeholder?: string
  hint?: string
  required?: boolean
  /** Show a clear (×) affordance so the value can be reset to "". */
  allowClear?: boolean
  disabled?: boolean
}

/**
 * Searchable ISO-3166-1 country picker. The user types a country name (or ISO
 * code) and the selection submits the alpha-2 code — e.g. "Nigeria" → "NG".
 */
export default function CountrySelect({
  id,
  label,
  value,
  onChange,
  placeholder = 'Search for a country…',
  hint,
  required,
  allowClear = false,
  disabled,
}: CountrySelectProps) {
  const [countries, setCountries] = useState<CountryDto[]>([])
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState(false)
  const [query, setQuery] = useState('')
  const [highlight, setHighlight] = useState(0)
  const rootRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  useEffect(() => {
    let active = true
    getCountries()
      .then((list) => {
        if (active) setCountries(list)
      })
      .catch(() => undefined)
    return () => {
      active = false
    }
  }, [])

  const selected = useMemo(() => countries.find((c) => c.alpha2 === value) ?? null, [countries, value])

  // Keep the input showing the selected country's friendly name when not typing.
  useEffect(() => {
    if (editing) return
    setQuery(selected?.name ?? '')
  }, [selected, editing])

  // Close when clicking/tapping outside the control.
  useEffect(() => {
    function onPointerDown(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) close()
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return countries
    return countries.filter(
      (c) =>
        c.name.toLowerCase().includes(q) ||
        c.alpha2.toLowerCase().includes(q) ||
        c.alpha3.toLowerCase().includes(q) ||
        c.numeric.includes(q),
    )
  }, [countries, query])

  const visible = filtered.slice(0, 100)

  function openList() {
    setOpen(true)
    setHighlight(0)
  }

  function close() {
    setOpen(false)
    setEditing(false)
    setQuery(selected?.name ?? '')
  }

  function selectCountry(country: CountryDto) {
    onChange(country.alpha2)
    setQuery(country.name)
    setEditing(false)
    setOpen(false)
  }

  function handleInputChange(next: string) {
    setEditing(true)
    setQuery(next)
    setOpen(true)
    setHighlight(0)
  }

  function handleKeyDown(event: ReactKeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      openList()
      setHighlight((h) => Math.min(h + 1, Math.max(visible.length - 1, 0)))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      openList()
      setHighlight((h) => Math.max(h - 1, 0))
    } else if (event.key === 'Enter') {
      const choice = visible[highlight]
      if (open && choice) {
        event.preventDefault()
        selectCountry(choice)
      }
    } else if (event.key === 'Escape') {
      event.preventDefault()
      close()
    }
  }

  // Keep the highlighted row in view inside the scrollable list.
  useEffect(() => {
    if (!open) return
    const activeRow = listRef.current?.querySelector('[data-active="true"]')
    activeRow?.scrollIntoView({ block: 'nearest' })
  }, [highlight, open])

  return (
    <div ref={rootRef} className="block">
      <label htmlFor={id} className={labelClass}>
        {label}
        {required ? <span className="text-red-500"> *</span> : null}
      </label>
      <div className="relative">
        <span className="pointer-events-none absolute inset-y-0 left-3.5 flex items-center text-stone-400">
          <SearchIcon className="h-4 w-4" />
        </span>
        <input
          id={id}
          role="combobox"
          aria-expanded={open}
          aria-autocomplete="list"
          aria-controls={open ? `${id}-listbox` : undefined}
          aria-activedescendant={open && visible[highlight] ? `${id}-option-${visible[highlight].alpha2}` : undefined}
          className={`${controlClass} pr-9 pl-10`}
          value={query}
          onChange={(e) => handleInputChange(e.target.value)}
          onFocus={() => {
            setEditing(true)
            openList()
          }}
          onBlur={(e) => {
            if (!rootRef.current?.contains(e.relatedTarget as Node)) close()
          }}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          disabled={disabled}
          autoComplete="off"
        />
        <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-stone-400">
          {allowClear && value ? (
            <button
              type="button"
              tabIndex={-1}
              aria-label="Clear country"
              className="pointer-events-auto grid h-6 w-6 place-items-center rounded-full text-stone-400 hover:bg-stone-100 hover:text-stone-600"
              onClick={(e) => {
                e.stopPropagation()
                onChange('')
                setQuery('')
                setEditing(false)
              }}
            >
              <XIcon className="h-4 w-4" />
            </button>
          ) : (
            <ChevronDownIcon className="h-4 w-4" />
          )}
        </span>

        {open ? (
          <ul
            id={`${id}-listbox`}
            ref={listRef}
            role="listbox"
            aria-label={label}
            className="absolute z-30 mt-1.5 max-h-72 w-full overflow-auto rounded-xl border border-stone-200 bg-white py-1 shadow-pop"
          >
            {visible.length === 0 ? (
              <li className="px-3.5 py-2.5 text-sm text-stone-500">No country matches “{query.trim()}”.</li>
            ) : (
              visible.map((country, index) => {
                const active = country.alpha2 === value
                const highlighted = index === highlight
                return (
                  <li
                    key={country.alpha2}
                    id={`${id}-option-${country.alpha2}`}
                    role="option"
                    aria-selected={active}
                    data-active={highlighted ? 'true' : undefined}
                    onMouseDown={(e) => {
                      e.preventDefault()
                      selectCountry(country)
                    }}
                    onMouseEnter={() => setHighlight(index)}
                    className={`flex cursor-pointer items-center justify-between gap-3 px-3.5 py-2 text-sm ${
                      highlighted ? 'bg-brand-50' : ''
                    } ${active ? 'font-semibold text-stone-900' : 'text-stone-700'}`}
                  >
                    <span className="min-w-0">
                      <span className="block truncate">{country.name}</span>
                      <span className="block text-xs font-normal tracking-wide text-stone-400">
                        {country.alpha2} · {country.alpha3} · {country.numeric}
                      </span>
                    </span>
                    <span className="grid h-5 w-5 shrink-0 place-items-center">
                      {active ? <CheckIcon className="h-4 w-4 text-brand-600" /> : null}
                    </span>
                  </li>
                )
              })
            )}
          </ul>
        ) : null}
      </div>
      {hint ? <span className="mt-1 block text-xs text-stone-500">{hint}</span> : null}
    </div>
  )
}