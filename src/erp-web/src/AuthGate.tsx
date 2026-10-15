import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { bootstrapAuth, getAuthAdapter } from './auth/runtime'
import type { AuthSession } from './auth/types'
import { getBuildInfo } from './buildInfo'

const buildInfo = getBuildInfo()

type GateState =
  | { status: 'loading' }
  | { status: 'ready'; session: AuthSession }
  | { status: 'error'; message: string }

function asErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback
}

export function AuthGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<GateState>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false
    bootstrapAuth()
      .then(({ session }) => {
        if (!cancelled) setState({ status: 'ready', session })
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
  }, [])

  if (state.status === 'loading') {
    return (
      <div className="auth-gate">
        <p>Anmeldung wird geprüft …</p>
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

  const adapter = getAuthAdapter()
  if (adapter.needsLogin(state.session)) {
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

  return (
    <>
      {children}
      {state.session.mode === 'cognito' ? (
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
      ) : null}
    </>
  )
}
