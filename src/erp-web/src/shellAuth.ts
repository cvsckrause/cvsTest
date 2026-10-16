import { createContext, useContext } from 'react'

export type ShellMembership = {
  tenantId: string
  isDefault: boolean
}

export type ShellAuth = {
  mode: 'cognito'
  memberships: ShellMembership[]
  tenantId: string
  switchTenant: (tenantId: string) => void
}

export const ShellAuthContext = createContext<ShellAuth | null>(null)

export function useShellAuth(): ShellAuth | null {
  return useContext(ShellAuthContext)
}
