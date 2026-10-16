import type { components } from './api.generated'

type Schemas = components['schemas']
export type ApprovalResponse = Schemas['ApprovalResponse']

const baseUrl = import.meta.env.VITE_API_URL ?? '/api/v1'

export type RegistrationOutcome = { ok: true } | { ok: false; message: string }

export type ApprovalOutcome =
  | { ok: true; mandantNumber: string; alreadyApproved: boolean }
  | { ok: false; message: string }

const UNREACHABLE = 'Der Server ist nicht erreichbar. Bitte später erneut versuchen.'
const TOO_MANY = 'Zu viele Anfragen von diesem Anschluss. Bitte in einer Stunde erneut versuchen.'

async function post(path: string, body: unknown): Promise<Response | null> {
  try {
    return await fetch(`${baseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(body),
    })
  } catch {
    return null
  }
}

async function problemDetail(response: Response): Promise<string | null> {
  try {
    const payload = (await response.json()) as { detail?: string }
    return payload.detail ?? null
  } catch {
    return null
  }
}

export async function registerMandant(request: Schemas['RegistrationRequest']): Promise<RegistrationOutcome> {
  const response = await post('/registrations', request)
  if (!response) return { ok: false, message: UNREACHABLE }
  if (response.ok) return { ok: true }
  if (response.status === 429) return { ok: false, message: TOO_MANY }
  if (response.status === 400) {
    return { ok: false, message: (await problemDetail(response)) ?? 'Bitte Firmenname und E-Mail prüfen.' }
  }
  return { ok: false, message: 'Die Registrierung ist gerade nicht möglich. Bitte später erneut versuchen.' }
}

export async function approveMandant(token: string): Promise<ApprovalOutcome> {
  const response = await post('/registrations/approve', { token } satisfies Schemas['ApprovalRequest'])
  if (!response) return { ok: false, message: UNREACHABLE }
  if (response.ok) {
    const payload = (await response.json()) as ApprovalResponse
    return { ok: true, mandantNumber: String(payload.mandantNumber), alreadyApproved: payload.alreadyApproved }
  }
  if (response.status === 404) return { ok: false, message: 'Dieser Freigabelink ist ungültig.' }
  if (response.status === 410) return { ok: false, message: 'Dieser Freigabelink ist abgelaufen.' }
  if (response.status === 429) return { ok: false, message: TOO_MANY }
  return { ok: false, message: 'Die Freigabe ist gerade nicht möglich. Bitte später erneut versuchen.' }
}

/** Reads `token` from a `#token=…` fragment; the fragment never reaches the server or its logs. */
export function approvalTokenFromHash(hash: string): string | null {
  const token = new URLSearchParams(hash.replace(/^#/, '')).get('token')
  return token && token.trim() ? token.trim() : null
}
