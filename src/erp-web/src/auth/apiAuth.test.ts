import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { erpApi } from '../api'
import { bootstrapAuth, resetAuthRuntime } from './runtime'
import type { AuthAdapter, AuthSession } from './types'

afterEach(() => {
  resetAuthRuntime()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  resetAuthRuntime()
})

test('createArticle sends Authorization when the cognito session has an access token', async () => {
  const session: AuthSession = {
    mode: 'cognito',
    authenticated: true,
    accessToken: 'access-token-1',
    tenantId: '11111111-1111-1111-1111-111111111111',
  }
  const adapter: AuthAdapter = {
    mode: 'cognito',
    ensureSession: async () => session,
    getAccessToken: async () => 'access-token-1',
    getTenantId: () => session.tenantId,
    setTenantId: (tenantId) => {
      session.tenantId = tenantId
    },
    login: async () => undefined,
    logout: async () => undefined,
    needsLogin: () => false,
  }

  await bootstrapAuth(
    async () => ({
      mode: 'cognito',
      authority: 'https://example.invalid',
      clientId: 'client',
      hostedUiOrigin: 'https://example.invalid',
    }),
    () => adapter,
  )

  const fetchMock = vi.fn(async () => ({
    ok: true,
    status: 201,
    headers: { get: () => 'application/json' },
    text: async () =>
      JSON.stringify({ id: 'a1', sku: 'SKU-1', name: 'Test', salesPrice: 10 }),
  }))
  vi.stubGlobal('fetch', fetchMock)

  await erpApi.createArticle('SKU-1', 'Test', 10, 0)

  expect(fetchMock).toHaveBeenCalled()
  const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
  const headers = init.headers as Record<string, string>
  expect(headers.Authorization).toBe('Bearer access-token-1')
  expect(headers['X-Tenant-Id']).toBe('11111111-1111-1111-1111-111111111111')
})
