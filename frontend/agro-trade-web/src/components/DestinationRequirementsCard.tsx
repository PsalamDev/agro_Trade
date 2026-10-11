import { useEffect, useState } from 'react'
import { Card } from './ui'
import CountrySelect from './CountrySelect'
import { getDestinationRequirements } from '../lib/intelligence'
import { problemFrom } from '../lib/api'
import { tradeDocumentLabel } from '../types/trade'
import type { DestinationRequirementsDto } from '../types/intelligence'

/**
 * The destination-requirements helper on the market intelligence page: pick an origin and a
 * destination and see the documents the route is likely to need. A curated guide rather than a
 * legal ruling, and anonymous reference information like the price series beside it.
 */
export function DestinationRequirementsCard() {
  const [origin, setOrigin] = useState('')
  const [destination, setDestination] = useState('')
  const [result, setResult] = useState<DestinationRequirementsDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<{ code?: string; detail?: string } | null>(null)

  // Fetch once both ends are chosen. Reading the route is cheap and side-effect free, so there is
  // no submit button — the answer follows the selection.
  useEffect(() => {
    if (origin.length !== 2 || destination.length !== 2) {
      setResult(null)
      setError(null)
      return
    }

    let cancelled = false
    setLoading(true)
    setError(null)
    getDestinationRequirements(origin, destination)
      .then((data) => {
        if (!cancelled) setResult(data)
      })
      .catch((err) => {
        if (!cancelled) {
          setResult(null)
          setError(problemFrom(err))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [origin, destination])

  return (
    <Card>
      <h2 className="font-display text-lg font-semibold text-stone-900">Destination requirements</h2>
      <p className="mt-1 text-sm text-stone-500">
        The documents a route is likely to need, from the countries the goods leave and arrive in.
      </p>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <CountrySelect
          id="requirements-origin"
          label="Origin"
          value={origin}
          onChange={setOrigin}
          placeholder="Country the goods leave from"
        />
        <CountrySelect
          id="requirements-destination"
          label="Destination"
          value={destination}
          onChange={setDestination}
          placeholder="Country the goods are bound for"
        />
      </div>

      {loading ? <p className="mt-4 text-sm text-stone-500">Checking…</p> : null}
      {error ? (
        <p className="mt-4 text-sm text-red-700">{error.detail ?? 'Could not check that route.'}</p>
      ) : null}

      {result && !loading ? (
        result.domestic ? (
          <p className="mt-4 rounded-xl border border-stone-200 bg-stone-50 px-4 py-3 text-sm text-stone-700">
            Domestic movement — no customs documents are required.
          </p>
        ) : (
          <div className="mt-4">
            <ul className="divide-y divide-stone-100 overflow-hidden rounded-xl border border-stone-200">
              {result.documents.map((doc) => (
                <li key={doc.document} className="flex items-start justify-between gap-3 px-3.5 py-3 text-sm">
                  <div className="min-w-0">
                    <p className="font-medium text-stone-800">{tradeDocumentLabel(doc.document)}</p>
                    <p className="mt-0.5 text-stone-500">{doc.reason}</p>
                  </div>
                  <span
                    className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] font-semibold tracking-wide uppercase ${
                      doc.mandatory ? 'bg-amber-100 text-amber-800' : 'bg-stone-100 text-stone-600'
                    }`}
                  >
                    {doc.mandatory ? 'Required' : 'Suggested'}
                  </span>
                </li>
              ))}
            </ul>
            {result.notes ? <p className="mt-3 text-xs text-stone-500">{result.notes}</p> : null}
          </div>
        )
      ) : null}
    </Card>
  )
}
