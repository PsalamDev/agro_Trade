import { useState } from 'react'
import type { FormEvent } from 'react'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { Card, Field, SubmitButton, TextArea } from '../components/ui'
import { ORG } from '../lib/brand'

const channels = [
  { label: 'Email', value: ORG.email, href: `mailto:${ORG.email}` },
  { label: 'Phone / WhatsApp', value: ORG.phone },
  { label: 'Head office', value: ORG.address },
]

export default function ContactPage() {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [message, setMessage] = useState('')
  const [sent, setSent] = useState(false)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const subject = encodeURIComponent(`Enquiry from ${name} — ${ORG.name}`)
    const body = encodeURIComponent(`${message}\n\n— ${name} (${email})`)
    window.location.href = `mailto:${ORG.email}?subject=${subject}&body=${body}`
    setSent(true)
  }

  return (
    <div className="min-h-screen">
      <AppHeader />

      <section className="bg-gradient-to-b from-brand-50 to-stone-50">
        <div className="mx-auto max-w-6xl px-4 py-20">
          <p className="mb-3 inline-flex rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-brand-800">
            Contact
          </p>
          <h1 className="max-w-2xl text-4xl font-bold leading-tight tracking-tight sm:text-5xl">
            Contact us
          </h1>
          <p className="mt-4 max-w-2xl text-lg text-stone-600">
            Questions about cocoa, cashew nuts, palm kernel, or fishery? Reach our trade desk for
            pricing, samples, and shipping terms.
          </p>
        </div>
      </section>

      <main className="mx-auto max-w-6xl px-4 py-16">
        <div className="grid gap-8 md:grid-cols-5">
          <div className="space-y-4 md:col-span-2">
            {channels.map((c) => (
              <Card key={c.label}>
                <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">{c.label}</p>
                {c.href ? (
                  <a href={c.href} className="mt-1 block font-semibold text-brand-700 hover:underline">
                    {c.value}
                  </a>
                ) : (
                  <p className="mt-1 font-semibold">{c.value}</p>
                )}
              </Card>
            ))}
          </div>

          <Card className="md:col-span-3">
            <h2 className="text-lg font-semibold">Send us a message</h2>
            <p className="mt-1 text-sm text-stone-500">
              Submitting opens your email app with the message pre-addressed to our trade desk.
            </p>
            <form onSubmit={handleSubmit} className="mt-6 space-y-4">
              <Field
                label="Your name"
                id="contact-name"
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
              <Field
                label="Email"
                id="contact-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              <TextArea
                label="Message"
                id="contact-message"
                required
                rows={6}
                value={message}
                onChange={(e) => setMessage(e.target.value)}
                placeholder="Products you're interested in, quantities, destination port…"
              />
              <SubmitButton type="submit">{sent ? 'Open your email app…' : 'Send message'}</SubmitButton>
            </form>
          </Card>
        </div>
      </main>

      <SiteFooter />
    </div>
  )
}