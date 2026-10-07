/**
 * Shared formatters for the admin surface (task 2.7).
 *
 * These exist as a module rather than as helpers duplicated per page because two of them encode
 * decisions that are easy to get subtly wrong, and getting them wrong is invisible.
 */

/**
 * Formats a UTC timestamp the way the server recorded it.
 *
 * <para>
 * The API emits `2026-09-27T09:00:25` — SQL Server's `datetime2` carries no offset, and
 * `System.Text.Json` only appends `Z` for a `DateTime` whose `Kind` is `Utc`, so nothing here
 * marks these as UTC. `new Date()` therefore reads them as *local* time and shifts every audit
 * entry by the reader's offset, which on a UTC+1 machine makes a review recorded at 09:00 look
 * like it happened at 10:00. Formatting with `timeZone: 'UTC'` renders the stored wall-clock
 * unchanged, which is the only reading that can be trusted.
 * </para>
 *
 * <para>
 * The value is labelled "UTC" rather than being silently localised: until the API carries an
 * offset, the honest thing is to say which zone the number is in rather than to imply the reader's.
 * </para>
 */
export function formatUtc(value: string | null | undefined): string {
  if (!value) return '—'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return '—'
  return `${parsed.toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  })} UTC`
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
