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

test('reuses the same Idempotency-Key when a receipt request fails and is retried', async () => {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
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
      return { ok: true, json: async () => [] }
    }
    if (url.includes('/stock/receipts')) {
      const attempts = fetchMock.mock.calls.filter(([requestUrl]) => String(requestUrl).includes('/stock/receipts')).length
      if (attempts === 1) {
        return {
          ok: false,
          status: 500,
          statusText: 'Server Error',
          json: async () => ({ error: 'temporary failure' }),
        }
      }
      return {
        ok: true,
        json: async () => ({
          id: 'receipt-1',
          articleId: 'article-1',
          sku: 'BIKE-001',
          warehouseCode: 'MAIN',
          quantity: 1,
          reference: null,
          occurredAt: '2026-10-14T09:00:00.000Z',
          replayed: false,
        }),
      }
    }
    if (url.includes('/stock')) {
      return { ok: true, json: async () => [] }
    }
    void init
    return {
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: async () => ({ error: 'not found' }),
    }
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })
  fireEvent.click(screen.getByTitle('Lager'))
  await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })

  fireEvent.click(screen.getByRole('button', { name: 'Wareneingang buchen' }))
  expect((await screen.findAllByText('temporary failure')).length).toBeGreaterThan(0)

  fireEvent.click(screen.getByRole('button', { name: 'Wareneingang buchen' }))
  expect((await screen.findAllByText(/Wareneingang 1 × BIKE-001 gebucht/)).length).toBeGreaterThan(0)

  const receiptCalls = fetchMock.mock.calls.filter(([requestUrl]) => String(requestUrl).includes('/stock/receipts'))
  expect(receiptCalls).toHaveLength(2)
  const firstKey = new Headers(receiptCalls[0][1]?.headers).get('Idempotency-Key')
  const secondKey = new Headers(receiptCalls[1][1]?.headers).get('Idempotency-Key')
  expect(firstKey).toBeTruthy()
  expect(secondKey).toBe(firstKey)
})

test('does not mint a new Idempotency-Key when stock refresh fails after a successful receipt', async () => {
  let receiptPosts = 0
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
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
    if (url.includes('/stock/receipts')) {
      receiptPosts += 1
      return {
        ok: true,
        json: async () => ({
          id: 'receipt-1',
          articleId: 'article-1',
          sku: 'BIKE-001',
          warehouseCode: 'MAIN',
          quantity: 1,
          reference: null,
          occurredAt: '2026-10-14T09:00:00.000Z',
          replayed: false,
        }),
      }
    }
    if (url.includes('/stock/movements') || url.includes('/stock')) {
      const receiptAlreadyPosted = receiptPosts > 0
      if (receiptAlreadyPosted && !url.includes('/stock/receipts')) {
        return {
          ok: false,
          status: 500,
          statusText: 'Server Error',
          json: async () => ({ error: 'refresh failed' }),
        }
      }
      return { ok: true, json: async () => [] }
    }
    void init
    return {
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: async () => ({ error: 'not found' }),
    }
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })
  fireEvent.click(screen.getByTitle('Lager'))
  await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })

  fireEvent.click(screen.getByRole('button', { name: 'Wareneingang buchen' }))
  expect((await screen.findAllByText(/Wareneingang 1 × BIKE-001 gebucht/)).length).toBeGreaterThan(0)
  expect(screen.getAllByText(/Anzeige konnte nicht aktualisiert werden/).length).toBeGreaterThan(0)

  fireEvent.click(screen.getByRole('button', { name: 'Wareneingang buchen' }))
  await screen.findAllByText(/Wareneingang 1 × BIKE-001 gebucht/)

  const receiptCalls = fetchMock.mock.calls.filter(([requestUrl]) => String(requestUrl).includes('/stock/receipts'))
  expect(receiptCalls).toHaveLength(2)
  const firstKey = new Headers(receiptCalls[0][1]?.headers).get('Idempotency-Key')
  const secondKey = new Headers(receiptCalls[1][1]?.headers).get('Idempotency-Key')
  expect(firstKey).toBeTruthy()
  expect(secondKey).not.toBe(firstKey)
})
