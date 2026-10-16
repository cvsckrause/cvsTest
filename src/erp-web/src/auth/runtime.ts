import { loadAuthConfig, selectAuthAdapter } from './selectAdapter'
import type { AuthAdapter, AuthSession } from './types'

let adapter: AuthAdapter | null = null
let session: AuthSession | null = null

export function getAuthAdapter(): AuthAdapter {
  if (!adapter) {
    throw new Error('Auth ist noch nicht initialisiert.')
  }
  return adapter
}

export function getAuthSession(): AuthSession | null {
  return session
}

export async function bootstrapAuth(
  loadConfig: typeof loadAuthConfig = loadAuthConfig,
  select: typeof selectAuthAdapter = selectAuthAdapter,
): Promise<{ adapter: AuthAdapter; session: AuthSession }> {
  // React StrictMode remounts effects in development; keep the adapter so its in-memory
  // tokens survive the remount.
  if (adapter) {
    session = await adapter.ensureSession()
    return { adapter, session }
  }

  try {
    const config = await loadConfig()
    adapter = select(config)
    session = await adapter.ensureSession()
    return { adapter, session }
  } catch (error) {
    adapter = null
    session = null
    throw error
  }
}

export async function authHeaders(): Promise<Record<string, string>> {
  if (!adapter) return {}
  const headers: Record<string, string> = {}
  const token = await adapter.getAccessToken()
  if (token) headers.Authorization = `Bearer ${token}`
  const tenantId = adapter.getTenantId()
  if (tenantId) headers['X-Tenant-Id'] = tenantId
  return headers
}

/** Test helper — reset module state between Vitest cases. */
export function resetAuthRuntime() {
  adapter = null
  session = null
}
