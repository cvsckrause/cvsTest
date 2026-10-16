import { useCallback, useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { LOGIN_FAILED } from './auth/cognitoAdapter'
import { bootstrapAuth } from './auth/runtime'
import type { AuthAdapter, AuthSession } from './auth/types'
import { getBuildInfo } from './buildInfo'
import type { components } from './api.generated'
import { ShellAuthContext } from './shellAuth'
import type { ShellAuth, ShellMembership } from './shellAuth'

const buildInfo = getBuildInfo()

type GateState =
  | { status: 'loading'; message: string }
  | { status: 'ready'; adapter: AuthAdapter; session: AuthSession; shell: ShellAuth | null }
  | { status: 'empty'; adapter: AuthAdapter }
  | { status: 'error'; message: string }

type Bootstrap = () => Promise<{ adapter: AuthAdapter; session: AuthSession }>

type MembershipPayload = components['schemas']['MembershipListResponse']

function asErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback
}

async function loadMemberships(adapter: AuthAdapter): Promise<MembershipPayload> {
  const token = await adapter.getAccessToken()
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  const base = import.meta.env.VITE_API_URL ?? '/api/v1'
  const response = await fetch(`${base}/me/memberships`, { headers })
  if (!response.ok) {
    throw new Error('Mandanten konnten nicht geladen werden.')
  }
  return response.json() as Promise<MembershipPayload>
}

function LogoutButton({ adapter }: { adapter: AuthAdapter }) {
  return (
    <button
      type="button"
      className="auth-logout"
      title="Abmelden"
      onClick={() => {
        void adapter.logout()
      }}
    >
      Abmelden
    </button>
  )
}

function LoginForm({
  adapter,
  onSignedIn,
}: {
  adapter: AuthAdapter
  onSignedIn: (session: AuthSession) => Promise<void>
}) {
  const [mandantennummer, setMandantennummer] = useState('')
  const [user, setUser] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    let session: AuthSession
    try {
      session = await adapter.login({ mandantennummer, user, password })
    } catch (failure: unknown) {
      setPassword('')
      setError(asErrorMessage(failure, LOGIN_FAILED))
      setBusy(false)
      return
    }
    await onSignedIn(session)
  }

  return (
    <div className="auth-gate">
      <h1>Northstar ERP</h1>
      <form className="auth-form" onSubmit={submit} noValidate>
        <label>
          Mandantennummer
          <input
            name="mandantennummer"
            inputMode="numeric"
            autoComplete="off"
            value={mandantennummer}
            onChange={(event) => setMandantennummer(event.target.value)}
          />
        </label>
        <label>
          User
          <input
            name="user"
            autoComplete="off"
            autoCapitalize="none"
            value={user}
            onChange={(event) => setUser(event.target.value)}
          />
        </label>
        <label>
          Passwort
          <input
            name="password"
            type="password"
            autoComplete="off"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        {error ? (
          <p className="auth-error" role="alert">
            {error}
          </p>
        ) : null}
        <button type="submit" className="primary" disabled={busy}>
          Anmelden
        </button>
      </form>
      <a className="auth-link" href="/registrieren">
        Neuen Mandanten erstellen
      </a>
      <p className="auth-build">{buildInfo.label}</p>
    </div>
  )
}

export function AuthGate({
  children,
  bootstrap = bootstrapAuth,
}: {
  children: ReactNode
  bootstrap?: Bootstrap
}) {
  const [state, setState] = useState<GateState>({
    status: 'loading',
    message: 'Anmeldung wird geprüft …',
  })

  const enter = useCallback(async (adapter: AuthAdapter, session: AuthSession, isCurrent: () => boolean) => {
    function shellFor(memberships: ShellMembership[], tenantId: string): ShellAuth {
      return {
        memberships,
        tenantId,
        switchTenant(nextTenantId) {
          adapter.setTenantId(nextTenantId)
          setState((current) => {
            if (current.status !== 'ready' || current.shell === null) return current
            return { ...current, shell: { ...current.shell, tenantId: nextTenantId } }
          })
        },
      }
    }

    try {
      if (session.mode !== 'cognito' || adapter.needsLogin(session)) {
        setState({ status: 'ready', adapter, session, shell: null })
        return
      }

      setState({ status: 'loading', message: 'Mandant wird ermittelt …' })
      const payload = await loadMemberships(adapter)
      if (!isCurrent()) return

      const memberships = payload.memberships ?? []
      const defaultTenantId = payload.defaultTenantId ?? null
      if (memberships.length === 0 || defaultTenantId === null) {
        setState({ status: 'empty', adapter })
        return
      }

      adapter.setTenantId(defaultTenantId)
      setState({ status: 'ready', adapter, session, shell: shellFor(memberships, defaultTenantId) })
    } catch (error: unknown) {
      if (isCurrent()) {
        setState({ status: 'error', message: asErrorMessage(error, LOGIN_FAILED) })
      }
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    const isCurrent = () => !cancelled

    bootstrap()
      .then(({ adapter, session }) => (cancelled ? undefined : enter(adapter, session, isCurrent)))
      .catch((error: unknown) => {
        if (!cancelled) {
          setState({ status: 'error', message: asErrorMessage(error, LOGIN_FAILED) })
        }
      })

    return () => {
      cancelled = true
    }
  }, [bootstrap, enter])

  if (state.status === 'loading') {
    return (
      <div className="auth-gate">
        <p>{state.message}</p>
        <p className="auth-build">{buildInfo.label}</p>
      </div>
    )
  }

  if (state.status === 'error') {
    return (
      <div className="auth-gate">
        <h1>Anmeldung</h1>
        <p className="auth-error">{state.message}</p>
        <button type="button" className="primary" onClick={() => window.location.reload()}>
          Neu laden
        </button>
      </div>
    )
  }

  if (state.status === 'empty') {
    return (
      <div className="auth-gate">
        <p>Diesem Benutzer ist kein Mandant zugeordnet.</p>
        <LogoutButton adapter={state.adapter} />
      </div>
    )
  }

  const { adapter, session, shell } = state
  if (adapter.needsLogin(session)) {
    return <LoginForm adapter={adapter} onSignedIn={(next) => enter(adapter, next, () => true)} />
  }

  const app = shell === null ? children : (
    <ShellAuthContext.Provider value={shell}>{children}</ShellAuthContext.Provider>
  )

  return (
    <>
      {app}
      {session.mode === 'cognito' ? <LogoutButton adapter={adapter} /> : null}
    </>
  )
}
