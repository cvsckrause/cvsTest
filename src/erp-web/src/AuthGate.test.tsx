import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, test } from 'vitest'
import { AuthGate } from './AuthGate'
import { resetAuthRuntime } from './auth/runtime'
import type { AuthAdapter, AuthSession } from './auth/types'

afterEach(() => {
  cleanup()
  resetAuthRuntime()
})

test('blocks the app behind Anmelden when Cognito session is unauthenticated', async () => {
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: false,
    accessToken: null,
    tenantId: null,
  }
  const adapter: AuthAdapter = {
    mode: 'cognito',
    ensureSession: async () => session,
    getAccessToken: async () => null,
    getTenantId: () => null,
    setTenantId: () => undefined,
    login: async () => undefined,
    logout: async () => undefined,
    needsLogin: (current) => !current.authenticated,
  }

  render(
    <AuthGate bootstrap={async () => ({ adapter, session })}>
      <div>secret-app</div>
    </AuthGate>,
  )

  expect(await screen.findByRole('button', { name: 'Anmelden' })).toBeInTheDocument()
  expect(screen.queryByText('secret-app')).not.toBeInTheDocument()
})
