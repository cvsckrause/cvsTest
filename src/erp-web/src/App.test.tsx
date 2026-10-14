import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import App from './App'

function jsonResponse(data: unknown, ok = true, status = ok ? 200 : 400) {
  const body = JSON.stringify(data)
  return {
    ok,
    status,
    statusText: ok ? 'OK' : 'Bad Request',
    headers: {
      get: (name: string) => (name.toLowerCase() === 'content-type' ? 'application/json' : null),
    },
    text: async () => body,
    json: async () => data,
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(jsonResponse([{ id: 'customer-1', number: '10000', name: 'Nordrad GmbH' }]))
    .mockResolvedValueOnce(jsonResponse([{ id: 'article-1', sku: 'BIKE-001', name: 'Cityrad', salesPrice: 799 }]))
    .mockResolvedValueOnce(jsonResponse([{
      id: 'address-1',
      name: 'Nordrad GmbH',
      street: 'Hafenstraße 12',
      postalCode: '20095',
      city: 'Hamburg',
      countryCode: 'DE',
      additionalLine: null,
    }]))
    .mockResolvedValue(jsonResponse([{
      id: 'link-1',
      customerId: 'customer-1',
      addressId: 'address-1',
      role: 'Shipping',
      address: {
        id: 'address-1',
        name: 'Nordrad GmbH',
        street: 'Hafenstraße 12',
        postalCode: '20095',
        city: 'Hamburg',
        countryCode: 'DE',
        additionalLine: null,
      },
    }])))
})

test('renders the order-entry workstation with ship-to', async () => {
  render(<App />)

  expect(screen.getByRole('heading', { name: 'Neuer Auftrag' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Auftrag speichern' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'Nordrad GmbH · 20095 Hamburg' })).toBeInTheDocument()
})

test('opens stamm workspace for articles and addresses', async () => {
  render(<App />)
  await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })

  fireEvent.click(screen.getAllByTitle('Stammdaten')[0])

  expect(await screen.findByRole('heading', { name: 'Artikel & Adressen' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Artikel anlegen' })).toBeInTheDocument()

  fireEvent.click(screen.getByRole('button', { name: /Adressen/ }))
  await waitFor(() => {
    expect(screen.getByRole('button', { name: 'Adresse anlegen' })).toBeInTheDocument()
  })
})
