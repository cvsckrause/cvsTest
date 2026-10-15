import { createCodeChallenge, createCodeVerifier, createOAuthState } from './pkce'
import type { AuthAdapter, AuthConfig, AuthSession, AuthTokens } from './types'

const PKCE_STORAGE_KEY = 'erp.auth.pkce'
const REFRESH_SKEW_MS = 60_000

type PkcePending = {
  verifier: string
  state: string
  redirectUri: string
}

type CognitoConfig = Extract<AuthConfig, { mode: 'cognito' }>

function redirectUri(): string {
  return `${window.location.origin}/`
}

function readPending(): PkcePending | null {
  const raw = sessionStorage.getItem(PKCE_STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as PkcePending
  } catch {
    return null
  }
}

function clearPending() {
  sessionStorage.removeItem(PKCE_STORAGE_KEY)
}

function parseCallback(search: string): { code: string; state: string } | null {
  const params = new URLSearchParams(search)
  const code = params.get('code')
  const state = params.get('state')
  if (!code || !state) return null
  return { code, state }
}

function clearCallbackFromUrl() {
  const url = new URL(window.location.href)
  if (!url.searchParams.has('code') && !url.searchParams.has('state')) return
  url.search = ''
  window.history.replaceState({}, document.title, url.pathname + url.hash)
}

async function exchangeToken(
  config: CognitoConfig,
  body: URLSearchParams,
): Promise<AuthTokens> {
  const response = await fetch(`${config.hostedUiOrigin}/oauth2/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
  })
  if (!response.ok) {
    throw new Error(`Token-Austausch fehlgeschlagen (${response.status}).`)
  }
  const payload = (await response.json()) as {
    access_token: string
    id_token?: string
    refresh_token?: string
    expires_in: number
  }
  return {
    accessToken: payload.access_token,
    idToken: payload.id_token,
    refreshToken: payload.refresh_token,
    expiresAt: Date.now() + payload.expires_in * 1000,
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
    if (!tokens?.refreshToken) {
      tokens = null
      return null
    }
    try {
      const next = await exchangeToken(
        config,
        new URLSearchParams({
          grant_type: 'refresh_token',
          client_id: config.clientId,
          refresh_token: tokens.refreshToken,
        }),
      )
      tokens = {
        ...next,
        refreshToken: next.refreshToken ?? tokens.refreshToken,
      }
      return tokens.accessToken
    } catch {
      tokens = null
      return null
    }
  }

  return {
    mode: 'cognito',
    async ensureSession() {
      const callback = parseCallback(window.location.search)
      if (callback) {
        const pending = readPending()
        clearPending()
        clearCallbackFromUrl()
        if (!pending || pending.state !== callback.state) {
          throw new Error('OAuth-State ist ungültig. Bitte erneut anmelden.')
        }
        tokens = await exchangeToken(
          config,
          new URLSearchParams({
            grant_type: 'authorization_code',
            client_id: config.clientId,
            code: callback.code,
            code_verifier: pending.verifier,
            redirect_uri: pending.redirectUri,
          }),
        )
      }
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
    async login() {
      const verifier = createCodeVerifier()
      const challenge = await createCodeChallenge(verifier)
      const state = createOAuthState()
      const redirect = redirectUri()
      sessionStorage.setItem(
        PKCE_STORAGE_KEY,
        JSON.stringify({ verifier, state, redirectUri: redirect } satisfies PkcePending),
      )
      const url = new URL(`${config.hostedUiOrigin}/oauth2/authorize`)
      url.searchParams.set('response_type', 'code')
      url.searchParams.set('client_id', config.clientId)
      url.searchParams.set('redirect_uri', redirect)
      url.searchParams.set('scope', 'openid email profile')
      url.searchParams.set('state', state)
      url.searchParams.set('code_challenge_method', 'S256')
      url.searchParams.set('code_challenge', challenge)
      window.location.assign(url.toString())
    },
    async logout() {
      tokens = null
      tenantId = null
      clearPending()
      const url = new URL(`${config.hostedUiOrigin}/logout`)
      url.searchParams.set('client_id', config.clientId)
      url.searchParams.set('logout_uri', redirectUri())
      window.location.assign(url.toString())
    },
    needsLogin(session) {
      return !session.authenticated
    },
  }
}
