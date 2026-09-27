import { useState } from 'react'
import type { FormEvent } from 'react'
import { AppHeader } from '../components/AppHeader'
import { SiteFooter } from '../components/SiteFooter'
import { Card, Field, SubmitButton, TextArea } from '../components/ui'
import { ORG } from '../lib/brand'

const channels = [
  { label: 'Phone / WhatsApp', value: ORG.phone, icon: PhoneIcon },
  { label: 'Email', value: ORG.email, href: `mailto:${ORG.email}`, icon: MailIcon },
  { label: 'Head office', value: ORG.address, icon: LocationIcon },
]

const iconClass = 'h-4 w-4'
const iconProps = {
  xmlns: 'http://www.w3.org/2000/svg',
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
} as const

function UserIcon() {
  return (
    <svg {...iconProps} className={iconClass}>
      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
      <circle cx="12" cy="7" r="4" />
    </svg>
  )
}

function MailIcon() {
  return (
    <svg {...iconProps} className={iconClass}>
      <rect x="2" y="4" width="20" height="16" rx="2" />
      <path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7" />
    </svg>
  )
}

function MessageIcon() {
  return (
    <svg {...iconProps} className={iconClass}>
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
    </svg>
  )
}

function PhoneIcon() {
  return (
    <svg {...iconProps} className={iconClass}>
      <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z" />
    </svg>
  )
}

function LocationIcon() {
  return (
    <svg {...iconProps} className={iconClass}>
      <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  )
}

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
                <div className="flex items-start gap-3">
                  <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-700">
                    <c.icon />
                  </span>
                  <div className="min-w-0">
                    <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">
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
                icon={<UserIcon />}
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
              <Field
                label="Email"
                id="contact-email"
                type="email"
                icon={<MailIcon />}
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              <TextArea
                label="Message"
                id="contact-message"
                icon={<MessageIcon />}
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