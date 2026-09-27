import { useState } from 'react'
import type { FormEvent } from 'react'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { Card, Field, SubmitButton, TextArea } from '../components/ui'
import { ORG } from '../lib/brand'
import { MailIcon, MapPinIcon, MessageIcon, PhoneIcon, UserIcon } from '../components/icons'

const channels = [
  { label: 'Phone / WhatsApp', value: ORG.phone, icon: PhoneIcon },
  { label: 'Email', value: ORG.email, href: `mailto:${ORG.email}`, icon: MailIcon },
  { label: 'Head office', value: ORG.address, icon: MapPinIcon },
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
    <div className="flex min-h-screen flex-col">
      <AppHeader />

      <section className="border-b border-stone-200 bg-gradient-to-b from-brand-50 to-stone-50">
        <div className="mx-auto max-w-6xl px-4 py-16">
          <p className="mb-3 inline-flex rounded-full bg-brand-100 px-3 py-1 text-xs font-semibold tracking-wide text-brand-800 uppercase">
            Contact
          </p>
          <h1 className="font-display max-w-2xl text-4xl font-extrabold tracking-tight sm:text-5xl">
            Talk to our trade desk
          </h1>
          <p className="mt-4 max-w-2xl text-lg leading-relaxed text-stone-600">
            Questions about cocoa, cashew nuts, palm kernel, or fishery? Reach our trade desk for
            pricing, samples, and shipping terms.
          </p>
        </div>
      </section>

      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-14">
        <div className="grid gap-6 md:grid-cols-5">
          <div className="space-y-4 md:col-span-2">
            {channels.map((c) => (
              <Card key={c.label} hover>
                <div className="flex items-start gap-3.5">
                  <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700">
                    <c.icon className="h-4.5 w-4.5" />
                  </span>
                  <div className="min-w-0">
                    <p className="text-xs font-semibold tracking-wide text-stone-500 uppercase">
                      {c.label}
                    </p>
                    {c.href ? (
                      <a
                        href={c.href}
                        className="mt-1 block font-semibold break-words text-brand-700 hover:underline"
                      >
                        {c.value}
                      </a>
                    ) : (
                      <p className="mt-1 font-semibold break-words">{c.value}</p>
                    )}
                  </div>
                </div>
              </Card>
            ))}

            <div className="rounded-2xl border border-brand-200 bg-gradient-to-br from-brand-50 to-white p-5">
              <h2 className="font-display text-sm font-semibold text-brand-900">Response times</h2>
              <p className="mt-1.5 text-sm leading-relaxed text-brand-800/80">
                We answer enquiries within one business day, Monday to Saturday. For urgent shipment
                matters, WhatsApp the number above.
              </p>
            </div>
          </div>

          <Card className="md:col-span-3">
            <h2 className="font-display text-lg font-semibold">Send us a message</h2>
            <p className="mt-1 text-sm text-stone-500">
              Submitting opens your email app with the message pre-addressed to our trade desk.
            </p>
            <form onSubmit={handleSubmit} className="mt-6 space-y-4">
              <Field
                label="Your name"
                id="contact-name"
                icon={<UserIcon className="h-4 w-4" />}
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
              <Field
                label="Email"
                id="contact-email"
                type="email"
                icon={<MailIcon className="h-4 w-4" />}
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              <TextArea
                label="Message"
                id="contact-message"
                icon={<MessageIcon className="h-4 w-4" />}
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
