import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import App from './App'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/customers')) {
      return {
        ok: true,
        json: async () => [{ id: 'customer-1', number: '10000', name: 'Nordrad GmbH' }],
      }
    }
    if (url.includes('/articles')) {
      return {
        ok: true,
        json: async () => [{ id: 'article-1', sku: 'BIKE-001', name: 'Cityrad', salesPrice: 799 }],
      }
    }
    if (url.includes('/stock/movements')) {
      return {
        ok: true,
        json: async () => [{
          id: 'movement-1',
          articleId: 'article-1',
          sku: 'BIKE-001',
          name: 'Cityrad',
          warehouseCode: 'MAIN',
          quantity: 5,
          movementType: 'GoodsReceipt',
          reference: 'WE-1',
          createdBy: 'tester',
          occurredAt: '2026-10-14T09:00:00.000Z',
        }],
      }
    }
    if (url.includes('/stock')) {
      return {
        ok: true,
        json: async () => [{
          articleId: 'article-1',
          sku: 'BIKE-001',
          name: 'Cityrad',
          warehouseCode: 'MAIN',
          onHand: 10,
          reserved: 2,
          available: 8,
        }],
      }
    }
    return {
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: async () => ({ error: 'not found' }),
    }
  }))
})

test('renders the order-entry workstation', async () => {
  render(<App />)

  expect(screen.getByRole('heading', { name: 'Neuer Auftrag' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Auftrag speichern' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })).toBeInTheDocument()
})

test('opens the lager workspace from the nav', async () => {
  render(<App />)

  await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })
  fireEvent.click(screen.getByTitle('Lager'))

  expect(await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Wareneingang buchen' })).toBeInTheDocument()

  const stockTable = await screen.findByRole('table', { name: 'Bestandübersicht' })
  expect(within(stockTable).getByText('BIKE-001 · Cityrad')).toBeInTheDocument()
  expect(within(stockTable).getByText('MAIN')).toBeInTheDocument()

  const movementTable = screen.getByRole('table', { name: 'Lagerbewegungen' })
  expect(within(movementTable).getByText('Wareneingang')).toBeInTheDocument()
  expect(within(movementTable).getByText('WE-1')).toBeInTheDocument()
})
