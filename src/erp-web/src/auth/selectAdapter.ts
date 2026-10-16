import { createCognitoAdapter } from './cognitoAdapter'
import { createLocalAdapter } from './localAdapter'
import type { AuthAdapter, AuthConfig } from './types'

export function selectAuthAdapter(config: AuthConfig): AuthAdapter {
  if (config.mode === 'cognito') {
    return createCognitoAdapter(config)
  }
  return createLocalAdapter()
}

export async function loadAuthConfig(fetcher: typeof fetch = fetch): Promise<AuthConfig> {
  const response = await fetcher('/auth-config.json', { headers: { Accept: 'application/json' } })
  if (!response.ok) {
    throw new Error(`Auth-Konfiguration nicht ladbar (${response.status}).`)
  }
  const payload = (await response.json()) as Partial<AuthConfig> & Record<string, unknown>
  if (payload.mode === 'cognito') {
    const authority = String(payload.authority ?? '')
    const clientId = String(payload.clientId ?? '')
    if (!authority || !clientId) {
      throw new Error('Cognito Auth-Konfiguration ist unvollständig.')
    }
    return { mode: 'cognito', authority, clientId }
  }
  return { mode: 'local' }
}
