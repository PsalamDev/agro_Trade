/**
 * Shared formatters for the admin surface (task 2.7).
 *
 * These exist as a module rather than as helpers duplicated per page because two of them encode
 * decisions that are easy to get subtly wrong, and getting them wrong is invisible.
 */

/**
 * Parses an API timestamp as the UTC instant it records.
 *
 * <para>
 * The API serialises `DateTime` values in ISO-8601. Every persisted timestamp is marked `Utc` in
 * the model, so `System.Text.Json` appends a trailing `Z` and `new Date()` reads the correct
 * instant. A value with no zone — an older row's cached response, or a non-conforming proxy —
 * would instead be read as *local* time and shifted by the reader's offset, so the missing `Z` is
 * restored here. Date-only strings already parse as UTC and are left alone.
 * </para>
 */
function parseUtc(value: string): Date {
  if (/[zZ]$|[+-]\d{2}:?\d{2}$/.test(value)) return new Date(value)
  return new Date(value.includes('T') ? `${value}Z` : value)
}

/**
 * Formats a UTC timestamp the way the server recorded it, labelled "UTC".
 *
 * <para>
 * The value is always shown in UTC rather than localised: the platform is cross-border, and a
 * reader's offset must not move a recorded moment. It is labelled rather than silent so the number
 * is never mistaken for the reader's own clock.
 * </para>
 */
export function formatUtc(value: string | null | undefined): string {
  if (!value) return '—'
  const parsed = parseUtc(value)
  if (Number.isNaN(parsed.getTime())) return '—'
  return `${parsed.toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  })} UTC`
}

/**
 * Formats the UTC calendar date of a timestamp, without a time.
 *
 * <para>
 * Used for fields that are dates in substance — an expiry, a deadline — where converting to the
 * reader's zone could shift the day across the date line. A price dated 2026-10-12 must read as
 * 2026-10-12 for everyone.
 * </para>
 */
export function formatUtcDate(value: string | null | undefined): string {
  if (!value) return '—'
  const parsed = parseUtc(value)
  if (Number.isNaN(parsed.getTime())) return '—'
  return parsed.toLocaleDateString(undefined, { dateStyle: 'medium', timeZone: 'UTC' })
}

/**
 * Formats an amount with its currency.
 *
 * <para>
 * The currency is always part of the result and never inferred from the caller's locale. The
 * platform holds no FX rate, so a figure is only ever meaningful alongside the unit it was
 * denominated in, and there is no "just show the number" version of this that does not invite the
 * reader to add it to something in another currency.
 * </para>
 */
export function money(amount: number, currency: string | null | undefined): string {
  const value = Number(amount ?? 0)
  const body = value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  return `${currency && currency !== 'Mixed' ? `${currency.toUpperCase()} ` : ''}${body}`
}

/** Counts with thousands separators. */
export function count(value: number | null | undefined): string {
  return Number(value ?? 0).toLocaleString()
}
