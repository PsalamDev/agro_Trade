export function SiteFooter() {
  return (
    <footer className="border-t border-stone-200 bg-white">
      <div className="mx-auto flex max-w-6xl flex-col items-center gap-2 px-4 py-8 text-sm text-stone-500 sm:flex-row sm:justify-between">
        <p>© {new Date().getFullYear()} Achievers Global Consult. Importer &amp; exporter of agro-allied products.</p>
        <p>Cocoa · Cashew nut · Palm kernel · Fishery</p>
      </div>
    </footer>
  )
}