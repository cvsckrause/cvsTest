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

test('a failed read names the load, and a problem detail wins over the fallback', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => ({
      ok: false,
      status: 403,
      headers: { get: () => '' },
      text: async () => '',
    })),
  )

  await expect(erpApi.customers()).rejects.toThrow('Laden fehlgeschlagen (403)')

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => ({
      ok: false,
      status: 403,
      headers: { get: () => 'application/problem+json' },
      text: async () => JSON.stringify({ detail: 'This user has no tenant membership.' }),
    })),
  )

  await expect(erpApi.articles()).rejects.toThrow('This user has no tenant membership.')
})

test('a failed save stays a save failure when the response has no detail', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => ({
      ok: false,
      status: 403,
      headers: { get: () => '' },
      text: async () => '',
    })),
  )

  await expect(erpApi.createArticle('SKU-1', 'Test', 10, 0)).rejects.toThrow('Speichern fehlgeschlagen (403)')
})
