import { afterEach, expect, test, vi } from 'vitest'
import { cognitoUsername, createCognitoAdapter } from './cognitoAdapter'

const config = {
  mode: 'cognito' as const,
  authority: 'https://cognito-idp.eu-north-1.amazonaws.com/eu-north-1_example',
  clientId: 'client-123',
}

afterEach(() => {
  vi.unstubAllGlobals()
})

function cognitoResponse(body: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: async () => body }
}

function sentBody(call: unknown[]) {
  const [, init] = call as [string, RequestInit]
  return JSON.parse(String(init.body)) as { AuthFlow: string; ClientId: string; AuthParameters: Record<string, string> }
}

test('login sends Mandantennummer.User with USER_PASSWORD_AUTH to the pool region', async () => {
  const fetchMock = vi.fn(async () =>
    cognitoResponse({ AuthenticationResult: { AccessToken: 'access-1', RefreshToken: 'refresh-1', ExpiresIn: 3600 } }),
  )
  vi.stubGlobal('fetch', fetchMock)
  const adapter = createCognitoAdapter(config)

  const session = await adapter.login({ mandantennummer: ' 1001 ', user: 'Kunde', password: 'Secret-123!' })

  expect(session.authenticated).toBe(true)
  expect(await adapter.getAccessToken()).toBe('access-1')
  const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
  expect(url).toBe('https://cognito-idp.eu-north-1.amazonaws.com/')
  expect(new Headers(init.headers).get('X-Amz-Target')).toBe('AWSCognitoIdentityProviderService.InitiateAuth')
  expect(sentBody(fetchMock.mock.calls[0])).toEqual({
    AuthFlow: 'USER_PASSWORD_AUTH',
    ClientId: 'client-123',
    AuthParameters: { USERNAME: '1001.kunde', PASSWORD: 'Secret-123!' },
  })
})

test('a rejected login reports only Anmeldung fehlgeschlagen.', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => cognitoResponse({ __type: 'NotAuthorizedException', message: 'Incorrect username or password.' }, 400)),
  )
  const adapter = createCognitoAdapter(config)

  await expect(adapter.login({ mandantennummer: '1001', user: 'kunde', password: 'wrong' })).rejects.toThrow(
    'Anmeldung fehlgeschlagen.',
  )
  expect((await adapter.ensureSession()).authenticated).toBe(false)
})

test('a login with a missing field fails without calling Cognito', async () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  const adapter = createCognitoAdapter(config)

  await expect(adapter.login({ mandantennummer: '', user: 'kunde', password: 'pw' })).rejects.toThrow(
    'Anmeldung fehlgeschlagen.',
  )
  expect(fetchMock).not.toHaveBeenCalled()
})

test('an access token close to expiry is refreshed with REFRESH_TOKEN_AUTH', async () => {
  const fetchMock = vi
    .fn()
    .mockResolvedValueOnce(
      cognitoResponse({ AuthenticationResult: { AccessToken: 'old', RefreshToken: 'refresh-1', ExpiresIn: 30 } }),
    )
    .mockResolvedValueOnce(cognitoResponse({ AuthenticationResult: { AccessToken: 'new', ExpiresIn: 3600 } }))
  vi.stubGlobal('fetch', fetchMock)
  const adapter = createCognitoAdapter(config)
  await adapter.login({ mandantennummer: '1001', user: 'kunde', password: 'pw' })

  expect(await adapter.getAccessToken()).toBe('new')
  expect(sentBody(fetchMock.mock.calls[1])).toEqual({
    AuthFlow: 'REFRESH_TOKEN_AUTH',
    ClientId: 'client-123',
    AuthParameters: { REFRESH_TOKEN: 'refresh-1' },
  })
})

test('the Cognito username keeps the number and lower-cases the user', () => {
  expect(cognitoUsername({ mandantennummer: '1001', user: ' KUNDE ', password: '' })).toBe('1001.kunde')
})
