import { afterEach, expect, test, vi } from 'vitest'
import { createLocalAdapter } from './localAdapter'
import { loadAuthConfig, selectAuthAdapter } from './selectAdapter'

afterEach(() => {
  vi.unstubAllGlobals()
})

test('selects the local adapter when auth-config mode is local', () => {
  const adapter = selectAuthAdapter({ mode: 'local' })
  expect(adapter.mode).toBe('local')
  expect(adapter.needsLogin({ mode: 'local', authenticated: true, accessToken: null, tenantId: null })).toBe(
    false,
  )
})

test('selects the cognito adapter when auth-config mode is cognito', () => {
  const adapter = selectAuthAdapter({
    mode: 'cognito',
    authority: 'https://cognito-idp.eu-north-1.amazonaws.com/eu-north-1_example',
    clientId: 'client-123',
  })
  expect(adapter.mode).toBe('cognito')
  expect(
    adapter.needsLogin({ mode: 'cognito', authenticated: false, accessToken: null, tenantId: null }),
  ).toBe(true)
})

test('loadAuthConfig maps a cognito payload from /auth-config.json without a Hosted UI origin', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => ({
      ok: true,
      json: async () => ({
        mode: 'cognito',
        authority: 'https://cognito-idp.eu-north-1.amazonaws.com/eu-north-1_example',
        clientId: 'client-123',
      }),
    })),
  )

  await expect(loadAuthConfig()).resolves.toEqual({
    mode: 'cognito',
    authority: 'https://cognito-idp.eu-north-1.amazonaws.com/eu-north-1_example',
    clientId: 'client-123',
  })
})

test('local adapter stays authenticated without a bearer token', async () => {
  const adapter = createLocalAdapter()
  const session = await adapter.ensureSession()
  expect(session.authenticated).toBe(true)
  expect(await adapter.getAccessToken()).toBeNull()
})
