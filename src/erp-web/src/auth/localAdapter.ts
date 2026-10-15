import type { AuthAdapter, AuthSession } from './types'

const localSession: AuthSession = {
  mode: 'local',
  authenticated: true,
  accessToken: null,
  tenantId: null,
}

export function createLocalAdapter(): AuthAdapter {
  let tenantId: string | null = null

  return {
    mode: 'local',
    async ensureSession() {
      return { ...localSession, tenantId }
    },
    async getAccessToken() {
      return null
    },
    getTenantId() {
      return tenantId
    },
    setTenantId(next) {
      tenantId = next
    },
    async login() {
      // Local DevelopmentIdentity — no interactive login.
    },
    async logout() {
      tenantId = null
    },
    needsLogin() {
      return false
    },
  }
}
