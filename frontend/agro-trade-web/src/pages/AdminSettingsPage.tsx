import { useCallback, useEffect, useState } from 'react'
import { AppHeader } from '../components/AppHeader'
import { AdminNav } from '../components/AdminNav'
import { Button, Card, ErrorAlert, InfoBanner, PageHeader, Spinner } from '../components/ui'
import { SettingsIcon } from '../components/icons'
import { adminGetSettings, adminUpdateSettings } from '../lib/trade'
import { problemFrom } from '../lib/api'
import { formatUtc } from '../lib/format'
import type { AdminSettingDto } from '../types/trade'

const controlClass =
  'rounded-xl border border-stone-300 bg-white px-3.5 py-2.5 text-sm shadow-soft transition duration-200 ease-soft outline-none placeholder:text-stone-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 disabled:bg-stone-100'

/**
 * Roles a default-registration setting may name.
 *
 * The registrable set, matching `RoleNames.Registrable` on the server: these are the roles a person
 * may hold by signing up. Offering a role nobody can register as would let an operator configure
 * something that fails closed at the next signup.
 */
const ROLE_OPTIONS = ['Buyer']

function optionsFor(setting: AdminSettingDto): string[] {
  if (!ROLE_OPTIONS.includes(setting.value)) {
    return [setting.value, ...ROLE_OPTIONS]
  }
  return ROLE_OPTIONS
}

export default function AdminSettingsPage() {
  const [settings, setSettings] = useState<AdminSettingDto[] | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [loadError, setLoadError] = useState<{ detail?: string } | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    setLoadError(null)
    try {
      const rows = await adminGetSettings()
      setSettings(rows)
      setDraft(Object.fromEntries(rows.map((row) => [row.key, row.value])))
    } catch (err) {
      setLoadError(problemFrom(err))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const dirty = (settings ?? []).filter((row) => draft[row.key] !== row.value && row.key in draft)

  async function save() {
    setSaving(true)
    setSaveError(null)
    setSaved(false)
    try {
      const updated = await adminUpdateSettings({
        settings: dirty.map((row) => ({ key: row.key, value: draft[row.key] })),
      })
      setSettings(updated)
      setDraft(Object.fromEntries(updated.map((row) => [row.key, row.value])))
      setSaved(true)
    } catch (err) {
      setSaveError(problemFrom(err).detail ?? 'The server refused the change.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="min-h-screen">
      <AppHeader />
      <main className="mx-auto max-w-3xl px-4 py-10">
        <PageHeader
          eyebrow="Administration"
          title="Settings"
          subtitle="Platform controls that running code reads. Each setting here changes behaviour the moment it is saved — a setting nothing consumes would be a screen that lies about its reach, so the catalogue is only what something honours."
        />

        <div className="mt-5">
          <AdminNav />
        </div>

        {loadError ? (
          <div className="mt-6">
            <ErrorAlert title="Could not load settings" detail={loadError.detail} />
          </div>
        ) : settings === null ? (
          <div className="mt-6">
            <Spinner label="Loading settings…" />
          </div>
        ) : (
          <div className="mt-6 space-y-4">
            {saved ? (
              <InfoBanner tone="brand" title="Saved">
                The change is live. It was validated before anything was written, and recorded in the
                audit trail as yours.
              </InfoBanner>
            ) : null}

            {saveError ? <ErrorAlert title="Could not save" detail={saveError} /> : null}

            {settings.map((setting) => (
              <SettingRow
                key={setting.key}
                setting={setting}
                value={draft[setting.key] ?? setting.value}
                onChange={(value) => {
                  setSaved(false)
                  setDraft((current) => ({ ...current, [setting.key]: value }))
                }}
              />
            ))}

            <div className="flex items-center justify-between gap-3 pt-1">
              <p className="text-xs text-stone-500">
                {dirty.length === 0
                  ? 'No unsaved changes.'
                  : `${dirty.length} unsaved change${dirty.length === 1 ? '' : 's'}.`}
              </p>
              <Button onClick={() => void save()} disabled={dirty.length === 0 || saving}>
                {saving ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </div>
        )}
      </main>
    </div>
  )
}

function SettingRow({
  setting,
  value,
  onChange,
}: {
  setting: AdminSettingDto
  value: string
  onChange: (value: string) => void
}) {
  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-stone-800">{setting.label}</p>
          <p className="mt-1 text-sm text-stone-500">{setting.description}</p>
          <p className="mt-2 font-mono text-[11px] text-stone-400">{setting.key}</p>
        </div>

        <div className="w-full sm:w-56">
          {setting.kind === 'boolean' ? (
            <select
              aria-label={setting.label}
              value={value}
              onChange={(e) => onChange(e.target.value)}
              className={`w-full ${controlClass}`}
            >
              <option value="true">Enabled</option>
              <option value="false">Disabled</option>
            </select>
          ) : setting.kind === 'role' ? (
            <select
              aria-label={setting.label}
              value={value}
              onChange={(e) => onChange(e.target.value)}
              className={`w-full ${controlClass}`}
            >
              {optionsFor(setting).map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          ) : (
            <input
              aria-label={setting.label}
              value={value}
              onChange={(e) => onChange(e.target.value)}
              className={`w-full ${controlClass}`}
            />
          )}
        </div>
      </div>

      <p className="mt-3 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-stone-400">
        <span className="inline-flex items-center gap-1">
          <SettingsIcon className="h-3.5 w-3.5" />
          {setting.isOverridden ? 'Changed from the default' : 'At the default'}
        </span>
        <span>
          Default <span className="font-medium text-stone-500">{setting.defaultValue || '—'}</span>
        </span>
        {setting.updatedAtUtc ? <span>Last changed {formatUtc(setting.updatedAtUtc)}</span> : null}
      </p>
    </Card>
  )
}
