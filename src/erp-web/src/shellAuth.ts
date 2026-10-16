import { createContext, useContext } from 'react'

export type ShellMembership = {
  tenantId: string
  isDefault: boolean
  mandantNumber?: number | string | null
  firmenname?: string | null
}

export type ShellAuth = {
  memberships: ShellMembership[]
  tenantId: string
  switchTenant: (tenantId: string) => void
}

export const ShellAuthContext = createContext<ShellAuth | null>(null)

export function useShellAuth(): ShellAuth | null {
  return useContext(ShellAuthContext)
}

export function mandantLabel(
  mandant: { tenantId: string; mandantNumber?: number | string | null; firmenname?: string | null },
): string {
  if (mandant.mandantNumber === null || mandant.mandantNumber === undefined) return `Mandant ${mandant.tenantId}`
  return mandant.firmenname
    ? `Mandant ${mandant.mandantNumber} · ${mandant.firmenname}`
    : `Mandant ${mandant.mandantNumber}`
}
