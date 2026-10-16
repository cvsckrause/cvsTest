import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
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

  useEffect(() => {
    let cancelled = false

    function shellFor(adapter: AuthAdapter, memberships: ShellMembership[], tenantId: string): ShellAuth {
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

    bootstrap()
      .then(async ({ adapter, session }) => {
        if (cancelled) return
        if (session.mode !== 'cognito' || adapter.needsLogin(session)) {
          setState({ status: 'ready', adapter, session, shell: null })
          return
        }

        setState({ status: 'loading', message: 'Mandant wird ermittelt …' })
        const payload = await loadMemberships(adapter)
        if (cancelled) return

        const memberships = payload.memberships ?? []
        const defaultTenantId = payload.defaultTenantId ?? null
        if (memberships.length === 0 || defaultTenantId === null) {
          setState({ status: 'empty', adapter })
          return
        }

        adapter.setTenantId(defaultTenantId)
        setState({
          status: 'ready',
          adapter,
          session,
          shell: shellFor(adapter, memberships, defaultTenantId),
        })
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setState({
            status: 'error',
            message: asErrorMessage(error, 'Anmeldung fehlgeschlagen.'),
          })
        }
      })

    return () => {
      cancelled = true
    }
  }, [bootstrap])

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
    return (
      <div className="auth-gate">
        <h1>Northstar ERP</h1>
        <p>Melde dich mit Cognito an, um Artikel und Aufträge zu bearbeiten.</p>
        <button
          type="button"
          className="primary"
          onClick={() => {
            void adapter.login().catch((error: unknown) => {
              setState({
                status: 'error',
                message: asErrorMessage(error, 'Login fehlgeschlagen.'),
              })
            })
          }}
        >
          Anmelden
        </button>
        <p className="auth-build">{buildInfo.label}</p>
      </div>
    )
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
