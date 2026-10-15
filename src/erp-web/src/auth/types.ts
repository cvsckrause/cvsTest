export type AuthMode = 'local' | 'cognito'

export type AuthConfig =
  | { mode: 'local' }
  | {
      mode: 'cognito'
      authority: string
      clientId: string
      hostedUiOrigin: string
    }

export type AuthTokens = {
  accessToken: string
  idToken?: string
  refreshToken?: string
  expiresAt: number
}

export type AuthSession = {
  mode: AuthMode
  authenticated: boolean
  accessToken: string | null
  tenantId: string | null
}

export type AuthAdapter = {
  mode: AuthMode
  /** Resolves once login/callback handling is done for this page load. */
  ensureSession(): Promise<AuthSession>
  getAccessToken(): Promise<string | null>
  getTenantId(): string | null
  setTenantId(tenantId: string | null): void
  login(): Promise<void>
  logout(): Promise<void>
  /** True when this adapter needs an interactive login before API calls. */
  needsLogin(session: AuthSession): boolean
}
