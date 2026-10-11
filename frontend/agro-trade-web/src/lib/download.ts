/**
 * Saves a fetched blob under a filename, without navigating the page away.
 *
 * <p>
 * A blob URL the browser is told to download, rather than a link to the endpoint: the export is
 * behind the bearer token the API client attaches in memory, and a link the browser opens itself
 * would send no Authorization header.
 * </p>
 */
export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()

  // Revoked on the next tick rather than immediately: Firefox and Safari can still be reading the
  // blob when the click returns, and a revoke that races the download cancels it.
  window.setTimeout(() => URL.revokeObjectURL(url), 0)
}
