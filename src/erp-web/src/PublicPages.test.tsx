import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, test, vi } from 'vitest'
import { ApprovalPage, RegistrationPage } from './PublicPages'
import { approvalTokenFromHash } from './registration'
import { publicPageFor } from './route'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  window.history.replaceState(null, '', '/')
})

function response(status: number, body: unknown = {}) {
  return { ok: status >= 200 && status < 300, status, json: async () => body }
}

test('only /registrieren and /freigabe bypass the login', () => {
  expect(publicPageFor('/registrieren')).toBe('registrieren')
  expect(publicPageFor('/freigabe')).toBe('freigabe')
  expect(publicPageFor('/sales')).toBeNull()
  expect(publicPageFor('/')).toBeNull()
})

test('registration requires Firmenname and E-Mail before it calls the API', async () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  render(<RegistrationPage />)

  fireEvent.change(screen.getByLabelText('Firmenname'), { target: { value: 'Muster GmbH' } })
  fireEvent.click(screen.getByRole('button', { name: 'Registrieren' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Firmenname und E-Mail sind Pflichtfelder.')
  expect(fetchMock).not.toHaveBeenCalled()
})

test('a submitted registration shows the confirmation', async () => {
  const fetchMock = vi.fn(async () => response(202, { status: 'pending' }))
  vi.stubGlobal('fetch', fetchMock)
  render(<RegistrationPage />)

  fireEvent.change(screen.getByLabelText('Firmenname'), { target: { value: ' Muster GmbH ' } })
  fireEvent.change(screen.getByLabelText('E-Mail'), { target: { value: 'info@muster.test' } })
  fireEvent.click(screen.getByRole('button', { name: 'Registrieren' }))

  expect(await screen.findByText('Anfrage gesendet')).toBeInTheDocument()
  expect(screen.getByText('info@muster.test')).toBeInTheDocument()
  const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
  expect(url).toBe('/api/v1/registrations')
  expect(JSON.parse(String(init.body))).toEqual({ firmenname: 'Muster GmbH', email: 'info@muster.test' })
})

test('the rate limit is explained instead of a bare status', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response(429)))
  render(<RegistrationPage />)

  fireEvent.change(screen.getByLabelText('Firmenname'), { target: { value: 'Muster GmbH' } })
  fireEvent.change(screen.getByLabelText('E-Mail'), { target: { value: 'info@muster.test' } })
  fireEvent.click(screen.getByRole('button', { name: 'Registrieren' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Zu viele Anfragen')
})

test('the approval page posts the token only when Freigeben is clicked', async () => {
  window.history.replaceState(null, '', '/freigabe#token=abc123')
  const fetchMock = vi.fn(async () => response(200, { mandantNumber: 1001, alreadyApproved: false }))
  vi.stubGlobal('fetch', fetchMock)
  render(<ApprovalPage />)

  const button = await screen.findByRole('button', { name: 'Freigeben' })
  expect(fetchMock).not.toHaveBeenCalled()
  expect(window.location.hash).toBe('')

  fireEvent.click(button)

  expect(await screen.findByText(/Mandant 1001 ist freigegeben/)).toBeInTheDocument()
  expect(fetchMock).toHaveBeenCalledTimes(1)
  const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
  expect(url).toBe('/api/v1/registrations/approve')
  expect(init.method).toBe('POST')
  expect(JSON.parse(String(init.body))).toEqual({ token: 'abc123' })
})

test('an expired approval link says so and keeps the button for a retry', async () => {
  window.history.replaceState(null, '', '/freigabe#token=old')
  vi.stubGlobal('fetch', vi.fn(async () => response(410)))
  render(<ApprovalPage />)

  fireEvent.click(await screen.findByRole('button', { name: 'Freigeben' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Dieser Freigabelink ist abgelaufen.')
})

test('a link without a token offers no approval', () => {
  window.history.replaceState(null, '', '/freigabe')
  render(<ApprovalPage />)

  expect(screen.getByText('Dieser Freigabelink ist ungültig.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Freigeben' })).not.toBeInTheDocument()
  expect(approvalTokenFromHash('#token=%20')).toBeNull()
})
