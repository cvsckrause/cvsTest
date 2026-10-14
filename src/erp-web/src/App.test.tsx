import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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

const customer = { id: 'customer-1', number: '10000', name: 'Nordrad GmbH' }
const article = { id: 'article-1', sku: 'BIKE-001', name: 'Cityrad', salesPrice: 799 }
const address = {
  id: 'address-1',
  name: 'Nordrad GmbH',
  street: 'Hafenstraße 12',
  postalCode: '20095',
  city: 'Hamburg',
  countryCode: 'DE',
  additionalLine: null,
}
const shippingLink = {
  id: 'link-1',
  customerId: 'customer-1',
  addressId: 'address-1',
  role: 'Shipping',
  address,
}
const stockRow = {
  articleId: 'article-1',
  sku: 'BIKE-001',
  name: 'Cityrad',
  warehouseCode: 'MAIN',
  onHand: 10,
  reserved: 2,
  available: 8,
}
const movement = {
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
}

function route(url: string) {
  if (url.includes('/customers/') && url.includes('/addresses')) return jsonResponse([shippingLink])
  if (url.includes('/addresses')) return jsonResponse([address])
  if (url.includes('/customers')) return jsonResponse([customer])
  if (url.includes('/articles')) return jsonResponse([article])
  if (url.includes('/stock/movements')) return jsonResponse([movement])
  if (url.includes('/stock')) return jsonResponse([stockRow])
  return jsonResponse({ error: 'not found' }, false, 404)
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => route(String(input))))
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
  const fetchMock = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/stock/receipts')) {
      const attempts = fetchMock.mock.calls.filter(([requestUrl]) => String(requestUrl).includes('/stock/receipts')).length
      if (attempts === 1) return jsonResponse({ error: 'temporary failure' }, false, 500)
      return jsonResponse({
        id: 'receipt-1',
        articleId: 'article-1',
        sku: 'BIKE-001',
        warehouseCode: 'MAIN',
        quantity: 1,
        reference: null,
        occurredAt: '2026-10-14T09:00:00.000Z',
        replayed: false,
      })
    }
    if (url.includes('/stock')) return jsonResponse([])
    return route(url)
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
  const fetchMock = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/stock/receipts')) {
      receiptPosts += 1
      return jsonResponse({
        id: 'receipt-1',
        articleId: 'article-1',
        sku: 'BIKE-001',
        warehouseCode: 'MAIN',
        quantity: 1,
        reference: null,
        occurredAt: '2026-10-14T09:00:00.000Z',
        replayed: false,
      })
    }
    if (url.includes('/stock')) {
      if (receiptPosts > 0) return jsonResponse({ error: 'refresh failed' }, false, 500)
      return jsonResponse([])
    }
    return route(url)
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
