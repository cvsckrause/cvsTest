import { render, screen } from '@testing-library/react'
import { beforeEach, expect, test, vi } from 'vitest'
import App from './App'

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce({
      ok: true,
      json: async () => [{ id: 'customer-1', number: '10000', name: 'Nordrad GmbH' }],
    })
    .mockResolvedValueOnce({
      ok: true,
      json: async () => [{ id: 'article-1', sku: 'BIKE-001', name: 'Cityrad', salesPrice: 799 }],
    }))
})

test('renders the order-entry workstation', async () => {
  render(<App />)

  expect(screen.getByRole('heading', { name: 'Neuer Auftrag' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Auftrag speichern' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })).toBeInTheDocument()
})
