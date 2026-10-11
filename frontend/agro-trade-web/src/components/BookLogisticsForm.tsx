import { useEffect, useMemo, useState } from 'react'
import { Button, ErrorAlert, Field, SelectField, TextArea } from './ui'
import { SearchIcon } from './icons'
import { getMyOrders, getSellingOrders, countryName } from '../lib/trade'
import { requestLogisticsBooking, searchLogisticsProviders } from '../lib/logistics'
import { problemFrom } from '../lib/api'
import { SHIPMENT_MODES, modeLabel } from '../types/logistics'
import type { LogisticsBookingDto, LogisticsProviderCardDto } from '../types/logistics'
import type { OrderCardDto } from '../types/trade'
import { ORDER_STATUS } from '../types/trade'

interface Props {
  /** When set, the booking is against this order and no order picker is shown. */
  orderId?: string
  orderNumber?: string
  /** When set, this provider is booked and no provider picker is shown. */
  providerId?: string
  providerName?: string
  /** A provider's declared modes, used to pick a sensible default. */
  providerModes?: number[]
  onBooked: (booking: LogisticsBookingDto) => void
  onClose?: () => void
}

/**
 * Raises a logistics booking. Either the order or the provider may be fixed by the page it is
 * shown on; whatever is missing becomes a picker. Both flows share one form so the request they
 * send is identical.
 */
export function BookLogisticsForm({
  orderId,
  orderNumber,
  providerId,
  providerName,
  providerModes,
  onBooked,
  onClose,
}: Props) {
  const needsProvider = !providerId
  const needsOrder = !orderId

  const [selectedProvider, setSelectedProvider] = useState<{ id: string; name: string; modes: number[] } | null>(
    providerId ? { id: providerId, name: providerName ?? 'this provider', modes: providerModes ?? [] } : null,
  )
  const [providerQuery, setProviderQuery] = useState('')
  const [providerResults, setProviderResults] = useState<LogisticsProviderCardDto[]>([])
  const [searching, setSearching] = useState(false)

  const [orders, setOrders] = useState<OrderCardDto[]>([])
  const [ordersLoading, setOrdersLoading] = useState(needsOrder)
  const [selectedOrderId, setSelectedOrderId] = useState(orderId ?? '')

  const [mode, setMode] = useState<number | ''>('')
  const [notes, setNotes] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string; fieldErrors?: Record<string, string[]> } | null>(
    null,
  )

  // A fixed provider's modes give a sensible default; otherwise the user picks one.
  useEffect(() => {
    if (providerModes?.length && mode === '') {
      setMode(providerModes[0])
    }
  }, [providerModes, mode])

  // Order picker: everything the caller is a party to, buyer side and seller side.
  useEffect(() => {
    if (!needsOrder) return
    let cancelled = false
    setOrdersLoading(true)
    Promise.all([getMyOrders({ pageSize: 50 }), getSellingOrders({ pageSize: 50 })])
      .then(([buying, selling]) => {
        if (cancelled) return
        const byId = new Map<string, OrderCardDto>()
        for (const order of [...buying.items, ...selling.items]) byId.set(order.id, order)
        // Only a live order is worth arranging carriage against; the API enforces this too.
        setOrders([...byId.values()].filter((order) => order.status !== ORDER_STATUS.Cancelled))
      })
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setOrdersLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [needsOrder])

  // Provider picker: a small debounced directory search.
  useEffect(() => {
    if (!needsProvider) return
    const term = providerQuery.trim()
    if (term.length < 2) {
      setProviderResults([])
      return
    }
    let cancelled = false
    setSearching(true)
    const handle = window.setTimeout(() => {
      searchLogisticsProviders({ q: term, pageSize: 8 })
        .then((result) => {
          if (!cancelled) setProviderResults(result.items)
        })
        .catch(() => undefined)
        .finally(() => {
          if (!cancelled) setSearching(false)
        })
    }, 250)
    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [needsProvider, providerQuery])

  const modeOptions = useMemo(() => {
    if (selectedProvider?.modes?.length) {
      return {
        labels: selectedProvider.modes.map(modeLabel),
        values: selectedProvider.modes.map((value) => value),
      }
    }
    return {
      labels: SHIPMENT_MODES.map((_, index) => modeLabel(index)),
      values: SHIPMENT_MODES.map((_, index) => index),
    }
  }, [selectedProvider])

  const chosenOrder = useMemo(
    () => orders.find((order) => order.id === selectedOrderId),
    [orders, selectedOrderId],
  )

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)

    const provider = selectedProvider
    const targetOrderId = orderId ?? selectedOrderId
    if (!provider) {
      setError({ detail: 'Choose a logistics provider.' })
      return
    }
    if (!targetOrderId) {
      setError({ detail: 'Choose an order to book for.' })
      return
    }
    if (mode === '') {
      setError({ detail: 'Choose a transport mode.' })
      return
    }

    setPending(true)
    try {
      const booking = await requestLogisticsBooking(targetOrderId, {
        providerId: provider.id,
        mode,
        notes: notes.trim() || null,
      })
      onBooked(booking)
    } catch (err) {
      setError(problemFrom(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} className="space-y-4">
      {needsProvider ? (
        <div>
          <Field
            label="Find a provider"
            id="book-provider"
            placeholder="Search by company name"
            icon={<SearchIcon className="h-4 w-4" />}
            value={providerQuery}
            onChange={(e) => setProviderQuery(e.target.value)}
            autoComplete="off"
          />
          {selectedProvider ? (
            <div className="mt-2 flex items-center justify-between rounded-xl border border-brand-200 bg-brand-50 px-3 py-2 text-sm">
              <span className="font-medium text-brand-900">{selectedProvider.name}</span>
              <button
                type="button"
                className="text-xs font-semibold text-brand-700 hover:underline"
                onClick={() => setSelectedProvider(null)}
              >
                Change
              </button>
            </div>
          ) : (
            <div className="mt-2 space-y-1">
              {searching ? <p className="px-1 text-xs text-stone-500">Searching…</p> : null}
              {!searching && providerQuery.trim().length >= 2 && providerResults.length === 0 ? (
                <p className="px-1 text-xs text-stone-500">No providers match that name.</p>
              ) : null}
              {providerResults.map((provider) => (
                <button
                  key={provider.id}
                  type="button"
                  onClick={() => {
                    setSelectedProvider({
                      id: provider.id,
                      name: provider.companyName,
                      modes: provider.modes,
                    })
                    setProviderResults([])
                    setProviderQuery('')
                  }}
                  className="flex w-full items-center justify-between rounded-xl border border-stone-200 bg-white px-3 py-2 text-left text-sm transition hover:border-brand-300 hover:bg-brand-50/40"
                >
                  <span className="font-medium text-stone-800">{provider.companyName}</span>
                  <span className="text-xs text-stone-500">
                    {countryName(provider.homeCountry)} · {provider.modes.map(modeLabel).join(', ') || 'No modes'}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      ) : (
        <p className="text-sm text-stone-600">
          Booking <span className="font-semibold text-stone-800">{providerName}</span>
          {orderNumber ? <> for order <span className="font-semibold text-stone-800">{orderNumber}</span></> : null}.
        </p>
      )}

      {needsOrder ? (
        <SelectField
          label="Order"
          id="book-order"
          options={
            ordersLoading
              ? ['Loading orders…']
              : orders.map((order) => `${order.orderNumber} · ${order.sellerTradingName || order.buyerDisplayName}`)
          }
          values={orders.map((order) => order.id)}
          value={selectedOrderId}
          disabled={ordersLoading}
          onChange={(e) => setSelectedOrderId(e.target.value)}
        />
      ) : null}

      <SelectField
        label="Transport mode"
        id="book-mode"
        options={modeOptions.labels}
        values={modeOptions.values}
        value={mode === '' ? '' : String(mode)}
        onChange={(e) => setMode(e.target.value === '' ? '' : Number(e.target.value))}
      />

      <TextArea
        label="Notes for the provider"
        id="book-notes"
        placeholder="Cargo details, collection window, special handling…"
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        rows={3}
      />

      {chosenOrder && !orderNumber ? (
        <p className="text-xs text-stone-500">
          Booking against {chosenOrder.orderNumber}
          {chosenOrder.destinationCountry ? ` · ${countryName(chosenOrder.destinationCountry)}` : ''}.
        </p>
      ) : null}

      {error ? <ErrorAlert title="Could not raise the booking" detail={error.detail} fieldErrors={error.fieldErrors} /> : null}

      <div className="flex items-center gap-2">
        <Button type="submit" disabled={pending}>
          {pending ? 'Requesting…' : 'Request booking'}
        </Button>
        {onClose ? (
          <Button type="button" variant="secondary" onClick={onClose} disabled={pending}>
            Cancel
          </Button>
        ) : null}
      </div>
    </form>
  )
}
