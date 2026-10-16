import { useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { approvalTokenFromHash, approveMandant, registerMandant } from './registration'
import type { ApprovalOutcome } from './registration'
import type { PublicPage } from './route'

export function PublicPageView({ page }: { page: PublicPage }) {
  return page === 'registrieren' ? <RegistrationPage /> : <ApprovalPage />
}

function PublicFrame({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="auth-gate">
      <h1>{title}</h1>
      {children}
      <a className="auth-link" href="/">
        Zur Anmeldung
      </a>
    </div>
  )
}

export function RegistrationPage() {
  const [firmenname, setFirmenname] = useState('')
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [submittedTo, setSubmittedTo] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const name = firmenname.trim()
    const address = email.trim()
    if (!name || !address) {
      setError('Firmenname und E-Mail sind Pflichtfelder.')
      return
    }

    setBusy(true)
    setError(null)
    const outcome = await registerMandant({ firmenname: name, email: address })
    setBusy(false)
    if (outcome.ok) {
      setSubmittedTo(address)
    } else {
      setError(outcome.message)
    }
  }

  if (submittedTo !== null) {
    return (
      <PublicFrame title="Anfrage gesendet">
        <p>
          Danke. Wir prüfen die Anfrage und senden die Zugangsdaten an <strong>{submittedTo}</strong>.
        </p>
      </PublicFrame>
    )
  }

  return (
    <PublicFrame title="Neuen Mandanten erstellen">
      <form className="auth-form" onSubmit={submit} noValidate>
        <label>
          Firmenname
          <input
            name="firmenname"
            autoComplete="organization"
            maxLength={240}
            value={firmenname}
            onChange={(event) => setFirmenname(event.target.value)}
          />
        </label>
        <label>
          E-Mail
          <input
            name="email"
            type="email"
            autoComplete="email"
            maxLength={320}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </label>
        {error ? (
          <p className="auth-error" role="alert">
            {error}
          </p>
        ) : null}
        <button type="submit" className="primary" disabled={busy}>
          Registrieren
        </button>
      </form>
    </PublicFrame>
  )
}

export function ApprovalPage() {
  const [token] = useState(() => approvalTokenFromHash(window.location.hash))
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<ApprovalOutcome | null>(null)

  useEffect(() => {
    if (window.location.hash) {
      window.history.replaceState(null, '', window.location.pathname)
    }
  }, [])

  if (token === null) {
    return (
      <PublicFrame title="Mandant freigeben">
        <p className="auth-error">Dieser Freigabelink ist ungültig.</p>
      </PublicFrame>
    )
  }

  if (outcome?.ok) {
    return (
      <PublicFrame title="Mandant freigeben">
        <p>
          {outcome.alreadyApproved
            ? `Mandant ${outcome.mandantNumber} war bereits freigegeben.`
            : `Mandant ${outcome.mandantNumber} ist freigegeben. Die Zugangsdaten wurden per E-Mail versendet.`}
        </p>
      </PublicFrame>
    )
  }

  return (
    <PublicFrame title="Mandant freigeben">
      <p>Die Freigabe legt den Zugang an und sendet die Zugangsdaten an den Kunden.</p>
      {outcome && !outcome.ok ? (
        <p className="auth-error" role="alert">
          {outcome.message}
        </p>
      ) : null}
      <button
        type="button"
        className="primary"
        disabled={busy}
        onClick={async () => {
          setBusy(true)
          setOutcome(await approveMandant(token))
          setBusy(false)
        }}
      >
        Freigeben
      </button>
    </PublicFrame>
  )
}
