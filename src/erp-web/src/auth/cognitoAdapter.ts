import type { AuthAdapter, AuthConfig, AuthSession, AuthTokens, LoginCredentials } from './types'

const REFRESH_SKEW_MS = 60_000
export const LOGIN_FAILED = 'Anmeldung fehlgeschlagen.'

type CognitoConfig = Extract<AuthConfig, { mode: 'cognito' }>

type AuthenticationResult = {
  AccessToken: string
  IdToken?: string
  RefreshToken?: string
  ExpiresIn: number
}

/** Registered logins are "{Mandantennummer}.{User}"; Cognito usernames are case-insensitive. */
export function cognitoUsername(credentials: LoginCredentials): string {
  return `${credentials.mandantennummer.trim()}.${credentials.user.trim().toLowerCase()}`
}

/** The authority is https://cognito-idp.{region}.amazonaws.com/{poolId}; the API lives at its origin. */
function identityProviderEndpoint(config: CognitoConfig): string {
  return `${new URL(config.authority).origin}/`
}

async function callCognito<T>(config: CognitoConfig, action: string, body: unknown): Promise<T> {
  const response = await fetch(identityProviderEndpoint(config), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-amz-json-1.1',
      'X-Amz-Target': `AWSCognitoIdentityProviderService.${action}`,
    },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    throw new Error(`Cognito ${action} failed (${response.status}).`)
  }
  return (await response.json()) as T
}

async function initiateAuth(
  config: CognitoConfig,
  authFlow: 'USER_PASSWORD_AUTH' | 'REFRESH_TOKEN_AUTH',
  parameters: Record<string, string>,
): Promise<AuthTokens> {
  const payload = await callCognito<{ AuthenticationResult?: AuthenticationResult; ChallengeName?: string }>(
    config,
    'InitiateAuth',
    { AuthFlow: authFlow, ClientId: config.clientId, AuthParameters: parameters },
  )
  const result = payload.AuthenticationResult
  if (!result?.AccessToken) {
    throw new Error(`Cognito answered with challenge ${payload.ChallengeName ?? 'none'}.`)
  }
  return {
    accessToken: result.AccessToken,
    idToken: result.IdToken,
    refreshToken: result.RefreshToken,
    expiresAt: Date.now() + result.ExpiresIn * 1000,
  }
}

export function createCognitoAdapter(config: CognitoConfig): AuthAdapter {
  let tokens: AuthTokens | null = null
  let tenantId: string | null = null
  let refreshInFlight: Promise<string | null> | null = null

  function sessionFromTokens(): AuthSession {
    return {
      mode: 'cognito',
      authenticated: Boolean(tokens?.accessToken),
      accessToken: tokens?.accessToken ?? null,
      tenantId,
    }
  }

  async function refreshAccessToken(): Promise<string | null> {
    const refreshToken = tokens?.refreshToken
    if (!refreshToken) {
      tokens = null
      return null
    }
    try {
      const next = await initiateAuth(config, 'REFRESH_TOKEN_AUTH', { REFRESH_TOKEN: refreshToken })
      tokens = { ...next, refreshToken: next.refreshToken ?? refreshToken }
      return tokens.accessToken
    } catch {
      tokens = null
      return null
    }
  }

  return {
    mode: 'cognito',
    async ensureSession() {
      return sessionFromTokens()
    },
    async getAccessToken() {
      if (!tokens?.accessToken) return null
      if (tokens.expiresAt - REFRESH_SKEW_MS > Date.now()) {
        return tokens.accessToken
      }
      if (!refreshInFlight) {
        refreshInFlight = refreshAccessToken().finally(() => {
          refreshInFlight = null
        })
      }
      return refreshInFlight
    },
    getTenantId() {
      return tenantId
    },
    setTenantId(next) {
      tenantId = next
    },
    async login(credentials) {
      if (!credentials.mandantennummer.trim() || !credentials.user.trim() || !credentials.password) {
        throw new Error(LOGIN_FAILED)
      }
      try {
        tokens = await initiateAuth(config, 'USER_PASSWORD_AUTH', {
          USERNAME: cognitoUsername(credentials),
          PASSWORD: credentials.password,
        })
      } catch {
        tokens = null
        throw new Error(LOGIN_FAILED)
      }
      return sessionFromTokens()
    },
    async logout() {
      const refreshToken = tokens?.refreshToken
      tokens = null
      tenantId = null
      if (refreshToken) {
        await callCognito(config, 'RevokeToken', { ClientId: config.clientId, Token: refreshToken }).catch(
          () => undefined,
        )
      }
      window.location.assign('/')
    },
    needsLogin(session) {
      return !session.authenticated
    },
  }
}
