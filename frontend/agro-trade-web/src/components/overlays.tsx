import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { NavLink } from 'react-router-dom'
import type { ReactNode, RefObject } from 'react'
import { Button } from './ui'
import { XIcon } from './icons'

/* -------------------------------------------------------------------------- */
/* Behaviour hooks                                                            */
/* -------------------------------------------------------------------------- */

/** Calls `onEscape` when Escape is pressed, while `active`. */
export function useEscape(onEscape: () => void, active = true) {
  const saved = useRef(onEscape)
  saved.current = onEscape

  useEffect(() => {
    if (!active) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation()
        saved.current()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [active])
}

/** Freezes background scroll (compensating for scrollbar width) while `active`. */
export function useBodyLock(active: boolean) {
  useEffect(() => {
    if (!active) return
    const { body, documentElement } = document
    const previousOverflow = body.style.overflow
    const previousPadding = body.style.paddingRight
    const scrollbar = window.innerWidth - documentElement.clientWidth

    body.style.overflow = 'hidden'
    if (scrollbar > 0) body.style.paddingRight = `${scrollbar}px`

    return () => {
      body.style.overflow = previousOverflow
      body.style.paddingRight = previousPadding
    }
  }, [active])
}

/** Calls `onOutside` for pointer presses outside `ref`, while `active`. */
export function useClickOutside<T extends HTMLElement>(
  ref: RefObject<T | null>,
  onOutside: () => void,
  active = true,
) {
  const saved = useRef(onOutside)
  saved.current = onOutside

  useEffect(() => {
    if (!active) return
    function onPointerDown(event: PointerEvent) {
      const element = ref.current
      if (element && !element.contains(event.target as Node)) saved.current()
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [ref, active])
}

/* -------------------------------------------------------------------------- */
/* Modal                                                                      */
/* -------------------------------------------------------------------------- */

const modalWidths = {
  sm: 'sm:max-w-sm',
  md: 'sm:max-w-lg',
  lg: 'sm:max-w-2xl',
  xl: 'sm:max-w-4xl',
} as const

export interface ModalProps {
  open: boolean
  onClose: () => void
  title: string
  description?: string
  children?: ReactNode
  footer?: ReactNode
  size?: keyof typeof modalWidths
  /** Set false for destructive flows that must be resolved explicitly. */
  dismissOnBackdrop?: boolean
}

export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  size = 'md',
  dismissOnBackdrop = true,
}: ModalProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEscape(onClose, open)
  useBodyLock(open)

  useEffect(() => {
    if (open) panelRef.current?.focus()
  }, [open])

  if (!open) return null

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div
        aria-hidden
        onClick={dismissOnBackdrop ? onClose : undefined}
        className={`absolute inset-0 bg-stone-900/45 backdrop-blur-sm ${dismissOnBackdrop ? 'animate-fade cursor-default' : 'animate-fade'}`}
      />
      <div
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        className={`relative flex max-h-[90vh] w-full ${modalWidths[size]} animate-zoom-in flex-col overflow-hidden rounded-t-3xl bg-white shadow-pop outline-none sm:rounded-2xl`}
      >
        <div className="flex items-start justify-between gap-4 border-b border-stone-100 px-6 py-4">
          <div className="min-w-0">
            <h2 id={titleId} className="font-display text-lg font-semibold">
              {title}
            </h2>
            {description ? (
              <p id={descriptionId} className="mt-1 text-sm text-stone-500">
                {description}
              </p>
            ) : null}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close dialog"
            className="-mt-1 grid h-8 w-8 shrink-0 place-items-center rounded-lg text-stone-400 transition hover:bg-stone-100 hover:text-stone-700"
          >
            <XIcon className="h-4 w-4" />
          </button>
        </div>

        {children ? <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">{children}</div> : null}

        {footer ? (
          <div className="flex flex-wrap justify-end gap-2 border-t border-stone-100 bg-stone-50/80 px-6 py-4">
            {footer}
          </div>
        ) : null}
      </div>
    </div>,
    document.body,
  )
}

export interface ConfirmDialogProps {
  open: boolean
  onCancel: () => void
  onConfirm: () => void
  title: string
  message: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  tone?: 'danger' | 'primary'
  pending?: boolean
}

export function ConfirmDialog({
  open,
  onCancel,
  onConfirm,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  tone = 'danger',
  pending = false,
}: ConfirmDialogProps) {
  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="sm"
      dismissOnBackdrop={!pending}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={pending}>
            {cancelLabel}
          </Button>
          <Button variant={tone === 'danger' ? 'danger' : 'primary'} onClick={onConfirm} disabled={pending}>
            {pending ? 'Working…' : confirmLabel}
          </Button>
        </>
      }
    >
      <div className="text-sm leading-relaxed text-stone-600">{message}</div>
    </Modal>
  )
}

/* -------------------------------------------------------------------------- */
/* Offcanvas (slide-in drawer)                                                */
/* -------------------------------------------------------------------------- */

export interface OffcanvasProps {
  open: boolean
  onClose: () => void
  title: string
  children: ReactNode
  footer?: ReactNode
  side?: 'right' | 'left'
  width?: string
}

export function Offcanvas({
  open,
  onClose,
  title,
  children,
  footer,
  side = 'right',
  width = 'max-w-sm',
}: OffcanvasProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()

  useEscape(onClose, open)
  useBodyLock(open)

  if (!open) return null

  const enter = side === 'right' ? 'animate-slide-in-right' : 'animate-slide-in-left'

  return createPortal(
    <div className="fixed inset-0 z-50 flex">
      <div aria-hidden onClick={onClose} className="animate-fade absolute inset-0 bg-stone-900/45 backdrop-blur-sm" />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className={`relative flex h-full w-full ${width} ${enter} flex-col bg-white shadow-pop`}
      >
        <div className="flex items-center justify-between gap-4 border-b border-stone-100 px-5 py-4">
          <h2 id={titleId} className="font-display text-base font-semibold">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close panel"
            className="grid h-8 w-8 shrink-0 place-items-center rounded-lg text-stone-400 transition hover:bg-stone-100 hover:text-stone-700"
          >
            <XIcon className="h-4 w-4" />
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>

        {footer ? <div className="border-t border-stone-100 px-5 py-4">{footer}</div> : null}
      </div>
    </div>,
    document.body,
  )
}

/* -------------------------------------------------------------------------- */
/* Dropdown menu                                                              */
/* -------------------------------------------------------------------------- */

export function MenuItem({
  to,
  icon,
  active,
  onClick,
  children,
}: {
  to?: string
  icon?: ReactNode
  active?: boolean
  onClick?: () => void
  children: ReactNode
}) {
  const base =
    'flex items-center gap-2.5 rounded-xl px-3 py-2 text-sm font-medium transition duration-200 ease-soft'
  const tone = active
    ? 'bg-brand-50 text-brand-700'
    : 'text-stone-600 hover:bg-stone-100 hover:text-stone-900'

  if (to) {
    return (
      <NavLink to={to} onClick={onClick} className={({ isActive }) => `${base} ${isActive || active ? 'bg-brand-50 text-brand-700' : tone}`}>
        {icon ? <span className="text-stone-400">{icon}</span> : null}
        {children}
      </NavLink>
    )
  }

  return (
    <button type="button" onClick={onClick} className={`${base} w-full ${tone}`}>
      {icon ? <span className="text-stone-400">{icon}</span> : null}
      {children}
    </button>
  )
}

export function Dropdown({
  label,
  button,
  buttonClassName = '',
  children,
  align = 'right',
  width = 'w-56',
  header,
}: {
  /** Accessible name for the trigger. */
  label: string
  /** Trigger content (text, icons, chevrons). */
  button: ReactNode
  buttonClassName?: string
  children: ReactNode
  align?: 'left' | 'right'
  width?: string
  /** Optional non-interactive heading rendered above the items. */
  header?: string
}) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const close = useCallback(() => setOpen(false), [])

  useClickOutside(rootRef, close, open)
  useEscape(close, open)

  return (
    <div className="relative" ref={rootRef}>
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label}
        onClick={() => setOpen((value) => !value)}
        className={buttonClassName}
      >
        {button}
      </button>
      {open ? (
        <div
          role="menu"
          className={`absolute top-full z-30 mt-2 ${width} origin-top animate-scale-in rounded-2xl border border-stone-200 bg-white p-1.5 shadow-pop ${
            align === 'right' ? 'right-0' : 'left-0'
          }`}
        >
          {header ? (
            <p className="px-3 pt-1.5 pb-2 text-[10px] font-semibold tracking-widest text-stone-400 uppercase">
              {header}
            </p>
          ) : null}
          {children}
        </div>
      ) : null}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Accordion                                                                  */
/* -------------------------------------------------------------------------- */

export interface AccordionItem {
  id: string
  title: ReactNode
  icon?: ReactNode
  meta?: ReactNode
  content: ReactNode
  defaultOpen?: boolean
  disabled?: boolean
}

/** Single-expand accordion; the open panel is tracked internally. */
export function Accordion({ items, className = '' }: { items: AccordionItem[]; className?: string }) {
  const firstOpen = items.find((item) => item.defaultOpen)?.id
  const [openId, setOpenId] = useState<string | null>(firstOpen ?? null)

  return (
    <div className={`divide-y divide-stone-100 overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-card ${className}`}>
      {items.map((item) => {
        const open = openId === item.id
        return (
          <div key={item.id}>
            <button
              type="button"
              disabled={item.disabled}
              aria-expanded={open}
              aria-controls={`accordion-panel-${item.id}`}
              onClick={() => setOpenId(open ? null : item.id)}
              className="flex w-full items-center gap-3 px-5 py-4 text-left transition hover:bg-stone-50 disabled:opacity-50"
            >
              {item.icon ? (
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700">
                  {item.icon}
                </span>
              ) : null}
              <span className="min-w-0 flex-1 text-sm font-semibold text-stone-800">{item.title}</span>
              {item.meta ? <span className="shrink-0 text-sm text-stone-500">{item.meta}</span> : null}
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden
                className={`h-4 w-4 shrink-0 text-stone-400 transition-transform duration-300 ease-soft ${open ? 'rotate-180' : ''}`}
              >
                <path d="m6 9 6 6 6-6" />
              </svg>
            </button>
            <div
              id={`accordion-panel-${item.id}`}
              role="region"
              className={`grid transition-[grid-template-rows] duration-300 ease-soft ${open ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}
            >
              <div className="overflow-hidden">
                <div className="border-t border-stone-100 px-5 py-4 text-sm leading-relaxed text-stone-600">
                  {item.content}
                </div>
              </div>
            </div>
          </div>
        )
      })}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Tabs                                                                       */
/* -------------------------------------------------------------------------- */

export interface TabItem {
  id: string
  label: string
  icon?: ReactNode
  count?: number
}

export function Tabs({
  items,
  value,
  onChange,
  className = '',
}: {
  items: TabItem[]
  value: string
  onChange: (id: string) => void
  className?: string
}) {
  return (
    <div role="tablist" className={`no-scrollbar flex gap-1 overflow-x-auto border-b border-stone-200 ${className}`}>
      {items.map((item) => {
        const active = item.id === value
        return (
          <button
            key={item.id}
            role="tab"
            type="button"
            aria-selected={active}
            onClick={() => onChange(item.id)}
            className={`relative flex shrink-0 items-center gap-2 px-4 py-2.5 text-sm font-semibold transition duration-200 ease-soft after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:origin-center after:rounded-full after:bg-brand-600 after:transition-transform after:duration-300 after:ease-soft ${
              active
                ? 'text-brand-700 after:scale-x-100'
                : 'text-stone-500 after:scale-x-0 hover:text-stone-900 hover:after:scale-x-100'
            }`}
          >
            {item.icon ? <span className={active ? 'text-brand-600' : 'text-stone-400'}>{item.icon}</span> : null}
            {item.label}
            {item.count !== undefined ? (
              <span
                className={`rounded-full px-1.5 py-0.5 text-[11px] font-bold ${
                  active ? 'bg-brand-100 text-brand-800' : 'bg-stone-100 text-stone-500'
                }`}
              >
                {item.count}
              </span>
            ) : null}
          </button>
        )
      })}
    </div>
  )
}

export function TabPanel({
  id,
  activeId,
  children,
}: {
  id: string
  activeId: string
  children: ReactNode
}) {
  if (id !== activeId) return null
  return (
    <div role="tabpanel" className="animate-fade-in pt-5">
      {children}
    </div>
  )
}

/* -------------------------------------------------------------------------- */
/* Tooltip                                                                    */
/* -------------------------------------------------------------------------- */

const tooltipPlacement = {
  top: 'bottom-full left-1/2 mb-2 -translate-x-1/2',
  bottom: 'top-full left-1/2 mt-2 -translate-x-1/2',
  left: 'right-full top-1/2 mr-2 -translate-y-1/2',
  right: 'left-full top-1/2 ml-2 -translate-y-1/2',
} as const

/**
 * CSS-only tooltip: wraps a single focusable trigger and reveals on hover or
 * keyboard focus. The bubble is non-interactive, so it never steals clicks.
 */
export function Tooltip({
  label,
  children,
  placement = 'top',
  className = '',
}: {
  label: string
  children: ReactNode
  placement?: keyof typeof tooltipPlacement
  className?: string
}) {
  return (
    <span className={`group/tip relative inline-flex ${className}`}>
      {children}
      <span
        role="tooltip"
        className={`pointer-events-none absolute z-50 w-max max-w-56 rounded-lg bg-stone-900 px-2.5 py-1.5 text-xs leading-snug font-medium text-white opacity-0 shadow-lift transition duration-200 ease-soft group-hover/tip:opacity-100 group-focus-within/tip:opacity-100 ${
          tooltipPlacement[placement]
        }`}
      >
        {label}
      </span>
    </span>
  )
}
