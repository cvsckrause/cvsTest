import { cleanup, fireEvent, render, screen } from '@testing-library/react'
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
    login: async () => session,
    logout: async () => undefined,
    needsLogin: (current) => session.mode === 'cognito' && !current.authenticated,
  }
}

const signedOut: AuthSession = {
  mode: 'cognito',
  authenticated: false,
  accessToken: null,
  tenantId: null,
}

test('blocks the app behind an empty three-field login form', async () => {
  render(
    <AuthGate bootstrap={async () => ({ adapter: adapterFor(signedOut), session: signedOut })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByRole('button', { name: 'Anmelden' })).toBeInTheDocument()
  expect(screen.getByLabelText('Mandantennummer')).toHaveValue('')
  expect(screen.getByLabelText('User')).toHaveValue('')
  expect(screen.getByLabelText('Passwort')).toHaveValue('')
  expect(screen.getByRole('link', { name: 'Neuen Mandanten erstellen' })).toHaveAttribute('href', '/registrieren')
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
})

test('submits all three fields and opens the shell for the signed-in Mandant', async () => {
  const signedIn: AuthSession = { ...signedOut, authenticated: true, accessToken: 'access-token' }
  const login = vi.fn<AuthAdapter['login']>(async () => signedIn)
  const adapter: AuthAdapter = { ...adapterFor(signedOut), login, needsLogin: (current) => !current.authenticated }
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => jsonResponse({ memberships: [{ tenantId: defaultTenantId, isDefault: true }], defaultTenantId })),
  )

  render(
    <AuthGate bootstrap={async () => ({ adapter, session: signedOut })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  fireEvent.change(await screen.findByLabelText('Mandantennummer'), { target: { value: '1001' } })
  fireEvent.change(screen.getByLabelText('User'), { target: { value: 'kunde' } })
  fireEvent.change(screen.getByLabelText('Passwort'), { target: { value: 'Secret-123!' } })
  fireEvent.click(screen.getByRole('button', { name: 'Anmelden' }))

  expect(await screen.findByText('secret-app')).toBeInTheDocument()
  expect(login).toHaveBeenCalledWith({ mandantennummer: '1001', user: 'kunde', password: 'Secret-123!' })
})

test('a failed login shows the error and clears only the password', async () => {
  const adapter: AuthAdapter = {
    ...adapterFor(signedOut),
    login: async () => {
      throw new Error('Anmeldung fehlgeschlagen.')
    },
  }

  render(
    <AuthGate bootstrap={async () => ({ adapter, session: signedOut })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  fireEvent.change(await screen.findByLabelText('Mandantennummer'), { target: { value: '1001' } })
  fireEvent.change(screen.getByLabelText('User'), { target: { value: 'kunde' } })
  fireEvent.change(screen.getByLabelText('Passwort'), { target: { value: 'wrong' } })
  fireEvent.click(screen.getByRole('button', { name: 'Anmelden' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Anmeldung fehlgeschlagen.')
  expect(screen.getByLabelText('Mandantennummer')).toHaveValue('1001')
  expect(screen.getByLabelText('Passwort')).toHaveValue('')
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
