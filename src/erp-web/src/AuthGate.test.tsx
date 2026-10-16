import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { AuthGate } from './AuthGate'
import { resetAuthRuntime } from './auth/runtime'
import type { AuthAdapter, AuthSession } from './auth/types'

const defaultTenantId = '22222222-2222-2222-2222-222222222222'

afterEach(() => {
  cleanup()
  resetAuthRuntime()
  vi.unstubAllGlobals()
})

function jsonResponse(data: unknown, ok = true, status = ok ? 200 : 400) {
  const body = JSON.stringify(data)
  return {
    ok,
    status,
    statusText: ok ? 'OK' : 'Bad Request',
    headers: {
      get: (name: string) => (name.toLowerCase() === 'content-type' ? 'application/json' : null),
    },
    text: async () => body,
    json: async () => data,
  }
}

function adapterFor(session: AuthSession, setTenantId: (tenantId: string | null) => void = () => undefined): AuthAdapter {
  return {
    mode: session.mode,
    ensureSession: async () => session,
    getAccessToken: async () => session.accessToken,
    getTenantId: () => session.tenantId,
    setTenantId,
    login: async () => undefined,
    logout: async () => undefined,
    needsLogin: (current) => session.mode === 'cognito' && !current.authenticated,
  }
}

test('blocks the app behind Anmelden when Cognito session is unauthenticated', async () => {
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: false,
    accessToken: null,
    tenantId: null,
  }

  render(
    <AuthGate bootstrap={async () => ({ adapter: adapterFor(session), session })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByRole('button', { name: 'Anmelden' })).toBeInTheDocument()
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
})

test('sets the server default tenant before rendering the shell', async () => {
  const events: string[] = []
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: true,
    accessToken: 'access-token',
    tenantId: null,
  }
  let release: (value: ReturnType<typeof jsonResponse>) => void = () => undefined
  const pending = new Promise<ReturnType<typeof jsonResponse>>((resolve) => {
    release = resolve
  })
  const fetchMock = vi.fn(() => pending)
  vi.stubGlobal('fetch', fetchMock)

  function Child() {
    events.push('child')
    return <div>secret-app</div>
  }

  render(
    <AuthGate
      bootstrap={async () => ({
        adapter: adapterFor(session, (tenantId) => {
          events.push(`set:${tenantId}`)
        }),
        session,
      })}
    >
      <Child />
    </AuthGate>,
  )

  expect(await screen.findByText('Mandant wird ermittelt …')).toBeInTheDocument()
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
  expect(fetchMock).toHaveBeenCalledTimes(1)
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
  expect(String(url)).toBe('/api/v1/me/memberships')
  expect(new Headers(init.headers).get('Authorization')).toBe('Bearer access-token')
  expect(new Headers(init.headers).has('X-Tenant-Id')).toBe(false)

  release(jsonResponse({
    memberships: [
      { tenantId: '11111111-1111-1111-1111-111111111111', isDefault: false },
      { tenantId: defaultTenantId, isDefault: true },
    ],
    defaultTenantId,
  }))

  expect(await screen.findByText('secret-app')).toBeInTheDocument()
  expect(events[0]).toBe(`set:${defaultTenantId}`)
  expect(events).toContain('child')
})

test('shows the empty-membership message and does not mount the shell', async () => {
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: true,
    accessToken: 'access-token',
    tenantId: null,
  }
  vi.stubGlobal('fetch', vi.fn(async () => jsonResponse({ memberships: [], defaultTenantId: null })))

  render(
    <AuthGate bootstrap={async () => ({ adapter: adapterFor(session), session })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByText('Diesem Benutzer ist kein Mandant zugeordnet.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Abmelden' })).toBeInTheDocument()
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
})

test('shows the auth error when memberships cannot be loaded', async () => {
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: true,
    accessToken: 'access-token',
    tenantId: null,
  }
  vi.stubGlobal('fetch', vi.fn(async () => jsonResponse({ detail: 'nope' }, false, 500)))

  render(
    <AuthGate bootstrap={async () => ({ adapter: adapterFor(session), session })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByText('Mandanten konnten nicht geladen werden.')).toBeInTheDocument()
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
})

test('renders local mode without requesting memberships', async () => {
  const session: AuthSession = {
    mode: 'local',
    authenticated: true,
    accessToken: null,
    tenantId: null,
  }
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)

  render(
    <AuthGate bootstrap={async () => ({ adapter: adapterFor(session), session })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByText('secret-app')).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
})
