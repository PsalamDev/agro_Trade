import { useState } from 'react'
import { Button } from './ui'
import { problemFrom } from '../lib/api'
import { downloadBlob } from '../lib/download'

/**
 * Fetches a CSV export and saves it, showing the server's reason if it fails.
 *
 * <p>
 * A button that fetches, rather than a link: the export is behind the bearer token, and a link the
 * browser follows would send no Authorization header and land on a 401. It also lets the export
 * report a transient failure in place, without a half-downloaded file.
 * </p>
 */
export function ExportButton({
  label,
  fileName,
  load,
  className = '',
}: {
  label: string
  fileName: string
  /** Fetches the file. Called fresh on each click so it captures the current filters. */
  load: () => Promise<Blob>
  className?: string
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run() {
    setBusy(true)
    setError(null)
    try {
      downloadBlob(await load(), fileName)
    } catch (err) {
      setError(problemFrom(err).detail)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={`flex flex-col items-end gap-1 ${className}`}>
      <Button variant="secondary" onClick={() => void run()} disabled={busy}>
        {busy ? 'Preparing…' : label}
      </Button>
      {error ? <p className="text-xs text-red-600">{error}</p> : null}
    </div>
  )
}
