import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import App from './App'
import { AuthGate } from './AuthGate'
import { bootstrapAuth, resetAuthRuntime } from './auth/runtime'
import type { AuthAdapter, AuthSession } from './auth/types'

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
const article = { id: 'article-1', sku: 'BIKE-001', name: 'Cityrad', salesPrice: 799, taxCode: 'Standard19' }
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
const goodsIssue = {
  ...movement,
  id: 'movement-2',
  quantity: 3,
  movementType: 'GoodsIssue',
  reference: 'DEL-SO-1001',
  occurredAt: '2026-10-14T10:00:00.000Z',
}

const salesOrder = {
  id: 'order-1',
  orderNumber: 'SO-1001',
  customerId: 'customer-1',
  shipToAddressId: 'address-1',
  shipTo: address,
  billTo: { buyerName: 'Nordrad GmbH', buyerNumber: '10000', addressId: 'address-1', address },
  status: 'Released',
  warehouseCode: 'MAIN',
  netAmount: 799,
  lines: [{ articleId: 'article-1', sku: 'BIKE-001', description: 'Cityrad', taxCode: 'Standard19', quantity: 1, unitPrice: 799, netAmount: 799 }],
  createdAt: '2026-10-15T10:00:00.000Z',
}

const salesOrderSummary = {
  id: 'order-1',
  orderNumber: 'SO-1001',
  customerId: 'customer-1',
  status: 'Released',
  netAmount: 799,
  createdAt: '2026-10-15T10:00:00.000Z',
}

const delivery = {
  id: 'delivery-1',
  salesOrderId: 'order-1',
  number: 'DEL-SO-1001',
  shipTo: address,
  createdAt: '2026-10-15T10:01:00.000Z',
}

const invoice = {
  id: 'invoice-1',
  salesOrderId: 'order-1',
  number: 'R-1',
  netAmount: 799,
  createdAt: '2026-10-15T10:01:00.000Z',
}

const localSession = {
  tenantId: '11111111-1111-1111-1111-111111111111',
  mandantNumber: 100,
  firmenname: 'Northstar Demo GmbH',
  userName: 'local-developer',
}

function route(url: string) {
  if (url.endsWith('/session')) return jsonResponse(localSession)
  if (url.includes('/sales-orders/') && url.includes('/deliveries')) return jsonResponse([delivery])
  if (url.includes('/sales-orders/') && url.includes('/invoices')) return jsonResponse([invoice])
  if (url.includes('/sales-orders/')) return jsonResponse(salesOrder)
  if (url.includes('/sales-orders')) return jsonResponse([salesOrderSummary])
  if (url.includes('/customers/') && url.includes('/addresses')) return jsonResponse([shippingLink])
  if (url.includes('/addresses')) return jsonResponse([address])
  if (url.includes('/customers')) return jsonResponse([customer])
  if (url.includes('/articles')) return jsonResponse([article])
  if (url.includes('/stock/movements')) return jsonResponse([goodsIssue, movement])
  if (url.includes('/stock')) return jsonResponse([stockRow])
  return jsonResponse({ error: 'not found' }, false, 404)
}

async function openNewOrder() {
  fireEvent.click(await screen.findByRole('button', { name: 'Neuer Auftrag' }))
  expect(await screen.findByRole('heading', { name: 'Neuer Auftrag' })).toBeInTheDocument()
}

afterEach(() => {
  cleanup()
  resetAuthRuntime()
  vi.unstubAllGlobals()
})

beforeEach(() => {
  window.history.replaceState(null, '', '/')
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => route(String(input))))
})

test('shows the problem detail when saving an article is rejected', async () => {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/articles') && init?.method === 'POST') {
      return jsonResponse({ title: 'The request conflicts with the current state.', status: 409, detail: 'An article with this SKU already exists.' }, false, 409)
    }
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  fireEvent.click(screen.getByTitle('Stammdaten'))
  await screen.findByRole('heading', { name: 'Artikel anlegen' })
  fireEvent.change(screen.getByLabelText('SKU'), { target: { value: 'BIKE-001' } })
  fireEvent.change(screen.getByLabelText('Bezeichnung'), { target: { value: 'Cityrad' } })
  fireEvent.click(screen.getByRole('button', { name: 'Artikel anlegen' }))

  expect((await screen.findAllByText('An article with this SKU already exists.')).length).toBeGreaterThan(0)
})

test('sends the chosen tax code when creating an article and lists the rate', async () => {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/articles') && init?.method === 'POST') return jsonResponse({ ...article, id: 'article-2', taxCode: 'Reduced7' }, true, 201)
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  fireEvent.click(screen.getByTitle('Stammdaten'))
  const articleTable = await screen.findByRole('table', { name: 'Artikelliste' })
  expect(within(articleTable).getByText('19 %')).toBeInTheDocument()

  fireEvent.change(screen.getByLabelText('SKU'), { target: { value: 'BOOK-001' } })
  fireEvent.change(screen.getByLabelText('Bezeichnung'), { target: { value: 'Fahrradbuch' } })
  fireEvent.change(screen.getByLabelText('Umsatzsteuer'), { target: { value: 'Reduced7' } })
  fireEvent.click(screen.getByRole('button', { name: 'Artikel anlegen' }))

  await waitFor(() => {
    const createCall = fetchMock.mock.calls.find(([requestUrl, requestInit]) =>
      String(requestUrl).includes('/articles') && requestInit?.method === 'POST')
    expect(JSON.parse(String(createCall?.[1]?.body))).toMatchObject({ sku: 'BOOK-001', taxCode: 'Reduced7' })
  })
})

test('keeps the article tax code when an article is edited', async () => {
  const reduced = { ...article, taxCode: 'Reduced7' }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/articles/article-1') && init?.method === 'PUT') return jsonResponse({ ...reduced, salesPrice: 849 })
    if (url.endsWith('/articles/article-1')) return jsonResponse(reduced)
    if (url.includes('/articles')) return jsonResponse([reduced])
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  fireEvent.click(screen.getByTitle('Stammdaten'))
  await screen.findByRole('heading', { name: 'Artikel anlegen' })
  fireEvent.click(screen.getByRole('button', { name: 'Bearbeiten' }))
  await screen.findByRole('heading', { name: 'Artikel bearbeiten' })
  await waitFor(() => {
    expect(screen.getByLabelText('Umsatzsteuer')).toHaveValue('Reduced7')
  })
  fireEvent.change(screen.getByLabelText('Verkaufspreis'), { target: { value: '849' } })
  fireEvent.click(screen.getByRole('button', { name: 'Artikel speichern' }))

  await waitFor(() => {
    const updateCall = fetchMock.mock.calls.find(([requestUrl, requestInit]) =>
      String(requestUrl).endsWith('/articles/article-1') && requestInit?.method === 'PUT')
    expect(JSON.parse(String(updateCall?.[1]?.body))).toMatchObject({ salesPrice: 849, taxCode: 'Reduced7' })
  })
})

test('tells a missing Mandant apart from a failed load on the Firma tab', async () => {
  let sellerStatus = 404
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.endsWith('/mandant/seller')) return jsonResponse({ detail: 'nope' }, false, sellerStatus)
    return route(url)
  }))
  window.history.replaceState(null, '', '/stamm/firma')

  render(<App />)
  expect(await screen.findByText('Für diesen Mandanten sind keine Firmendaten hinterlegt.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Firmendaten speichern' })).not.toBeInTheDocument()

  cleanup()
  sellerStatus = 500
  render(<App />)
  expect(await screen.findByText(/Firmendaten konnten nicht geladen werden/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Firmendaten speichern' })).not.toBeInTheDocument()
})

test('edits the seller details on the Firma tab with a read-only Firmenname', async () => {
  const seller = {
    mandantNumber: 100,
    firmenname: 'Northstar Demo GmbH',
    street: null,
    postalCode: null,
    city: null,
    countryCode: null,
    additionalLine: null,
    steuernummer: null,
    ustIdNr: null,
    missingFields: ['street', 'postalCode', 'city', 'countryCode', 'steuernummerOrUstIdNr'],
  }
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/mandant/seller') && init?.method === 'PUT') {
      return jsonResponse({ ...seller, ...JSON.parse(String(init.body)), ustIdNr: 'DE123456789', missingFields: [] })
    }
    if (url.endsWith('/mandant/seller')) return jsonResponse(seller)
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)
  window.history.replaceState(null, '', '/stamm/firma')

  render(<App />)
  const firmenname = await screen.findByDisplayValue('Northstar Demo GmbH')
  expect(firmenname).toBeDisabled()
  expect(window.location.pathname).toBe('/stamm/firma')

  fireEvent.change(screen.getByLabelText('Straße'), { target: { value: 'Hafenstraße 1' } })
  fireEvent.change(screen.getByLabelText('PLZ'), { target: { value: '20095' } })
  fireEvent.change(screen.getByLabelText('Ort'), { target: { value: 'Hamburg' } })
  fireEvent.change(screen.getByLabelText('USt-IdNr.'), { target: { value: 'de 123 456 789' } })
  fireEvent.click(screen.getByRole('button', { name: 'Firmendaten speichern' }))

  expect((await screen.findAllByText('Firmendaten gespeichert.')).length).toBeGreaterThan(0)
  const putCall = fetchMock.mock.calls.find(([requestUrl, requestInit]) =>
    String(requestUrl).endsWith('/mandant/seller') && requestInit?.method === 'PUT')
  expect(JSON.parse(String(putCall?.[1]?.body))).toEqual({
    street: 'Hafenstraße 1',
    postalCode: '20095',
    city: 'Hamburg',
    countryCode: 'DE',
    additionalLine: null,
    steuernummer: null,
    ustIdNr: 'de 123 456 789',
  })
  expect(screen.getByLabelText('USt-IdNr.')).toHaveValue('DE123456789')
})

test('rounds each draft line to cents before summing the order net', async () => {
  const bolt = { id: 'article-2', sku: 'BOLT-01', name: 'Schraube', salesPrice: 0.125, taxCode: 'Reduced7' }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/articles')) return jsonResponse([bolt])
    return route(url)
  }))

  render(<App />)
  await openNewOrder()
  await screen.findByRole('option', { name: 'BOLT-01 · Schraube' })
  fireEvent.click(screen.getByRole('button', { name: 'Position hinzufügen' }))
  fireEvent.click(screen.getByRole('button', { name: 'Position hinzufügen' }))

  const lineTable = screen.getByRole('table', { name: 'Auftragspositionen' })
  const [, firstLine] = within(lineTable).getAllByRole('row')
  expect(firstLine.querySelector('strong:last-of-type')).toHaveTextContent('0,13 €')
  const warenwert = screen.getByText('Warenwert').parentElement as HTMLElement
  expect(within(warenwert).getByText('0,26 €')).toBeInTheDocument()
})

test('shows the sales order list and opens capture from it', async () => {
  render(<App />)

  expect(await screen.findByRole('heading', { name: 'Auftragsliste' })).toBeInTheDocument()
  expect(await screen.findByText('SO-1001')).toBeInTheDocument()
  expect(screen.getByLabelText(/^Build /)).toHaveTextContent('vdev · local · unknown')

  await openNewOrder()
  expect(screen.getByRole('button', { name: 'Auftrag speichern' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'BIKE-001 · Cityrad' })).toBeInTheDocument()
  expect(await screen.findByRole('option', { name: 'Nordrad GmbH · 20095 Hamburg' })).toBeInTheDocument()
})

test('opens an order from the list and shows delivery and invoice numbers', async () => {
  render(<App />)

  fireEvent.click(await screen.findByRole('button', { name: 'Öffnen' }))
  expect(await screen.findByRole('heading', { name: 'SO-1001' })).toBeInTheDocument()
  expect(await screen.findByText('DEL-SO-1001')).toBeInTheDocument()
  expect(screen.getByText('R-1')).toBeInTheDocument()
  expect(screen.getByText('Lieferung DEL-SO-1001, Rechnung R-1.')).toBeInTheDocument()
  expect(window.location.pathname).toBe('/sales/order-1')
})

test('opens stamm workspace for articles and addresses', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Auftragsliste' })

  fireEvent.click(screen.getAllByTitle('Stammdaten')[0])

  expect(await screen.findByRole('heading', { name: 'Artikel, Adressen & Firma' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Artikel anlegen' })).toBeInTheDocument()

  fireEvent.click(screen.getByRole('button', { name: /Adressen/ }))
  await waitFor(() => {
    expect(screen.getByRole('button', { name: 'Adresse anlegen' })).toBeInTheDocument()
  })
})

test('opens the lager workspace from the nav', async () => {
  render(<App />)

  await screen.findByRole('heading', { name: 'Auftragsliste' })
  fireEvent.click(screen.getByTitle('Lager'))

  expect(await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Wareneingang buchen' })).toBeInTheDocument()

  const stockTable = await screen.findByRole('table', { name: 'Bestandübersicht' })
  expect(within(stockTable).getByText('BIKE-001 · Cityrad')).toBeInTheDocument()
  expect(within(stockTable).getByText('MAIN')).toBeInTheDocument()

  const movementTable = screen.getByRole('table', { name: 'Lagerbewegungen' })
  expect(within(movementTable).getByText('Wareneingang')).toBeInTheDocument()
  expect(within(movementTable).getByText('Warenausgang')).toBeInTheDocument()
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
  await screen.findByRole('heading', { name: 'Auftragsliste' })
  fireEvent.click(screen.getByTitle('Lager'))
  await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })

  // While the workspace loads, the submit button reads "Bucht …".
  fireEvent.click(await screen.findByRole('button', { name: 'Wareneingang buchen' }))
  expect((await screen.findAllByText('temporary failure')).length).toBeGreaterThan(0)

  fireEvent.click(await screen.findByRole('button', { name: 'Wareneingang buchen' }))
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
  await screen.findByRole('heading', { name: 'Auftragsliste' })
  fireEvent.click(screen.getByTitle('Lager'))
  await screen.findByRole('heading', { name: 'Bestand & Wareneingang' })

  fireEvent.click(await screen.findByRole('button', { name: 'Wareneingang buchen' }))
  expect((await screen.findAllByText(/Wareneingang 1 × BIKE-001 gebucht/)).length).toBeGreaterThan(0)
  expect(screen.getAllByText(/Anzeige konnte nicht aktualisiert werden/).length).toBeGreaterThan(0)

  fireEvent.click(await screen.findByRole('button', { name: 'Wareneingang buchen' }))
  await screen.findAllByText(/Wareneingang 1 × BIKE-001 gebucht/)

  const receiptCalls = fetchMock.mock.calls.filter(([requestUrl]) => String(requestUrl).includes('/stock/receipts'))
  expect(receiptCalls).toHaveLength(2)
  const firstKey = new Headers(receiptCalls[0][1]?.headers).get('Idempotency-Key')
  const secondKey = new Headers(receiptCalls[1][1]?.headers).get('Idempotency-Key')
  expect(firstKey).toBeTruthy()
  expect(secondKey).not.toBe(firstKey)
})

test('opens the workspace and document named in the URL', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  render(<App />)

  expect(await screen.findByRole('heading', { name: 'SO-1001' })).toBeInTheDocument()
  expect(screen.getAllByText('Freigegeben').length).toBeGreaterThan(0)
  expect(window.location.pathname).toBe('/sales/order-1')

  window.history.pushState(null, '', '/stamm/articles/article-1')
  window.dispatchEvent(new PopStateEvent('popstate'))
  expect(await screen.findByRole('heading', { name: 'Artikel bearbeiten' })).toBeInTheDocument()
  expect(screen.getByDisplayValue('BIKE-001')).toBeInTheDocument()
})

test('sends NEBEN when the warehouse field is neben', async () => {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/sales-orders') && init?.method === 'POST') {
      return jsonResponse({ ...salesOrder, status: 'Draft', warehouseCode: 'NEBEN' }, true, 201)
    }
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  await openNewOrder()
  await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })
  await screen.findByRole('option', { name: 'Nordrad GmbH · 20095 Hamburg' })
  fireEvent.change(screen.getByLabelText('Lager'), { target: { value: 'neben' } })
  expect(screen.queryByLabelText('Rechnungsadresse')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Position hinzufügen' }))
  fireEvent.click(screen.getByRole('button', { name: 'Auftrag speichern' }))

  await screen.findByRole('button', { name: 'Auftrag freigeben' })
  const createCall = fetchMock.mock.calls.find(([requestUrl, requestInit]) =>
    String(requestUrl).endsWith('/sales-orders') && requestInit?.method === 'POST')
  expect(createCall).toBeTruthy()
  const body = JSON.parse(String(createCall?.[1]?.body)) as {
    warehouseCode: string
    orderNumber: string | null
    billToAddressId: string | null
  }
  expect(body.warehouseCode).toBe('NEBEN')
  expect(body.orderNumber).toBeNull()
  expect(body.billToAddressId).toBeNull()
})

test('asks for a bill-to address when the customer has several and sends the choice', async () => {
  const branch = { ...address, id: 'address-2', name: 'Nordrad Buchhaltung', street: 'Kontorweg 3', postalCode: '20457' }
  const billingLinks = [
    { ...shippingLink, id: 'link-b1', role: 'Billing' },
    { id: 'link-b2', customerId: 'customer-1', addressId: 'address-2', role: 'Billing', address: branch },
  ]
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/customers/customer-1/addresses')) return jsonResponse([shippingLink, ...billingLinks])
    if (url.endsWith('/sales-orders') && init?.method === 'POST') {
      return jsonResponse({
        ...salesOrder,
        status: 'Draft',
        billTo: { buyerName: 'Nordrad GmbH', buyerNumber: '10000', addressId: 'address-2', address: branch },
      }, true, 201)
    }
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  await openNewOrder()
  await screen.findByRole('option', { name: '10000 · Nordrad GmbH' })
  const billTo = await screen.findByLabelText('Rechnungsadresse')
  expect(billTo).toHaveValue('')
  fireEvent.click(screen.getByRole('button', { name: 'Position hinzufügen' }))
  fireEvent.click(screen.getByRole('button', { name: 'Auftrag speichern' }))

  expect(await screen.findByText('Bitte eine Rechnungsadresse wählen.')).toBeInTheDocument()
  expect(billTo).toHaveAttribute('aria-invalid', 'true')
  const posts = () => fetchMock.mock.calls.filter(([requestUrl, requestInit]) =>
    String(requestUrl).endsWith('/sales-orders') && requestInit?.method === 'POST')
  expect(posts()).toHaveLength(0)

  fireEvent.change(billTo, { target: { value: 'address-2' } })
  expect(screen.queryByText('Bitte eine Rechnungsadresse wählen.')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Auftrag speichern' }))

  await screen.findByRole('button', { name: 'Auftrag freigeben' })
  expect(posts()).toHaveLength(1)
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ billToAddressId: 'address-2' })
  expect(screen.getByText('Nordrad GmbH · Kontorweg 3, 20457 Hamburg')).toBeInTheDocument()
})

test('points to the Firma tab when a release is refused for missing seller details', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/release') && init?.method === 'POST') {
      return jsonResponse({
        error: 'Seller details are incomplete. Complete them under Stammdaten > Firma before releasing orders.',
        missingSellerFields: ['street', 'steuernummerOrUstIdNr'],
      }, false, 409)
    }
    if (url.endsWith('/mandant/seller')) {
      return jsonResponse({
        mandantNumber: 100,
        firmenname: 'Northstar Demo GmbH',
        street: null,
        postalCode: null,
        city: null,
        countryCode: 'DE',
        additionalLine: null,
        steuernummer: null,
        ustIdNr: null,
        missingFields: ['street', 'postalCode', 'city', 'steuernummerOrUstIdNr'],
      })
    }
    if (url.includes('/sales-orders/') && !url.includes('/deliveries') && !url.includes('/invoices')) {
      return jsonResponse({ ...salesOrder, status: 'Draft' })
    }
    return route(url)
  }))

  render(<App />)
  fireEvent.click(await screen.findByRole('button', { name: 'Auftrag freigeben' }))

  const warning = await screen.findByRole('alert')
  expect(warning).toHaveTextContent('Ohne vollständige Firmendaten kann keine Rechnung erstellt werden.')
  expect(screen.getAllByText('Firmendaten unvollständig. Unter Stammdaten › Firma ergänzen, dann erneut freigeben.').length)
    .toBeGreaterThan(0)
  expect(screen.getByText('Gespeichert')).toBeInTheDocument()

  fireEvent.click(within(warning).getByRole('button', { name: 'Firmendaten ergänzen' }))
  expect(await screen.findByRole('heading', { name: 'Firmendaten für Rechnungen' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/stamm/firma')
})

test('cancels a released order and offers a new one', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/cancel') && init?.method === 'POST') {
      return jsonResponse({ ...salesOrder, status: 'Cancelled' })
    }
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<App />)
  expect(await screen.findByText('Freigegeben')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Stornieren' }))

  expect(await screen.findByText('Storniert')).toBeInTheDocument()
  expect(screen.getAllByText('Der Auftrag ist storniert. Eine Lieferung wird nicht erzeugt.').length).toBeGreaterThan(0)
  expect(screen.getByRole('button', { name: 'Neuen Auftrag erfassen' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Auftrag freigeben' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Stornieren' })).not.toBeInTheDocument()

  const cancelCall = fetchMock.mock.calls.find(([requestUrl]) => String(requestUrl).includes('/cancel'))
  expect(cancelCall).toBeTruthy()
  expect(new Headers(cancelCall?.[1]?.headers).get('Idempotency-Key')).toBeTruthy()
})

test('keeps a released order when cancel is rejected', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/cancel') && init?.method === 'POST') {
      return jsonResponse({ detail: 'Delivered orders cannot be cancelled.' }, false, 409)
    }
    return route(url)
  }))

  render(<App />)
  expect(await screen.findByText('Freigegeben')).toBeInTheDocument()
  expect((await screen.findAllByText('Lieferung DEL-SO-1001 und Rechnung R-1 liegen vor.')).length).toBeGreaterThan(0)
  fireEvent.click(screen.getByRole('button', { name: 'Stornieren' }))

  expect((await screen.findAllByText('Delivered orders cannot be cancelled.')).length).toBeGreaterThan(0)
  expect(screen.getByText('Freigegeben')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Stornieren' })).toBeInTheDocument()
})

test('hides release and cancel on a cancelled order', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/sales-orders/')) return jsonResponse({ ...salesOrder, status: 'Cancelled' })
    return route(url)
  }))

  render(<App />)
  expect(await screen.findByText('Storniert')).toBeInTheDocument()
  expect(screen.getAllByText('Der Auftrag ist storniert. Eine Lieferung wird nicht erzeugt.').length).toBeGreaterThan(0)
  expect(screen.getByRole('button', { name: 'Neuen Auftrag erfassen' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Auftrag freigeben' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Stornieren' })).not.toBeInTheDocument()
})

const tenantA = '11111111-1111-1111-1111-111111111111'
const tenantB = '22222222-2222-2222-2222-222222222222'

type TestMembership = {
  tenantId: string
  isDefault: boolean
  mandantNumber?: number | null
  firmenname?: string | null
}

async function bootCognito(
  defaultTenantId: string,
  memberships: TestMembership[],
  session: unknown = { tenantId: defaultTenantId, mandantNumber: null, firmenname: null, userName: 'kunde' },
) {
  let tenantId: string | null = null
  const authSession: AuthSession = {
    mode: 'cognito',
    authenticated: true,
    accessToken: 'access-token',
    tenantId: null,
  }
  const adapter: AuthAdapter = {
    mode: 'cognito',
    ensureSession: async () => authSession,
    getAccessToken: async () => 'access-token',
    getTenantId: () => tenantId,
    setTenantId: (next) => {
      tenantId = next
    },
    login: async () => authSession,
    logout: async () => undefined,
    needsLogin: () => false,
  }
  await bootstrapAuth(
    async () => ({
      mode: 'cognito',
      authority: 'https://auth.example.test',
      clientId: 'client',
    }),
    () => adapter,
  )
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/me/memberships')) {
      return jsonResponse({ memberships, defaultTenantId })
    }
    if (url.endsWith('/session')) {
      return session === null ? jsonResponse({ detail: 'down' }, false, 500) : jsonResponse(session)
    }
    return route(url)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

test('shows the local Mandant from the session', async () => {
  render(<App />)
  expect(await screen.findByText('Mandant 100 · Northstar Demo GmbH')).toBeInTheDocument()
  expect(screen.queryByRole('combobox', { name: 'Mandant wechseln' })).not.toBeInTheDocument()
})

test('shows the Mandant number and Firmenname for one membership and no switcher', async () => {
  await bootCognito(
    tenantA,
    [{ tenantId: tenantA, isDefault: true, mandantNumber: 1001, firmenname: 'Nordlicht GmbH' }],
    { tenantId: tenantA, mandantNumber: 1001, firmenname: 'Nordlicht GmbH', userName: 'kunde' },
  )
  render(<AuthGate><App /></AuthGate>)

  expect(await screen.findByText('Mandant 1001 · Nordlicht GmbH')).toBeInTheDocument()
  expect(screen.queryByRole('combobox', { name: 'Mandant wechseln' })).not.toBeInTheDocument()
})

test('falls back to the tenant id when the Mandant is not registered', async () => {
  await bootCognito(tenantA, [{ tenantId: tenantA, isDefault: true }])
  render(<AuthGate><App /></AuthGate>)

  expect(await screen.findByText(`Mandant ${tenantA}`)).toBeInTheDocument()
})

test('shows a neutral Mandant line when the session cannot be loaded', async () => {
  await bootCognito(tenantA, [{ tenantId: tenantA, isDefault: true }], null)
  render(<AuthGate><App /></AuthGate>)

  expect(await screen.findByText('Mandant –')).toBeInTheDocument()
})

test('labels the Mandant switcher with number and Firmenname', async () => {
  await bootCognito(tenantA, [
    { tenantId: tenantA, isDefault: true, mandantNumber: 1001, firmenname: 'Nordlicht GmbH' },
    { tenantId: tenantB, isDefault: false, mandantNumber: 1002, firmenname: 'Südwind AG' },
  ])
  render(<AuthGate><App /></AuthGate>)

  const select = await screen.findByRole('combobox', { name: 'Mandant wechseln' })
  expect(within(select).getByRole('option', { name: 'Mandant 1001 · Nordlicht GmbH' })).toBeInTheDocument()
  expect(within(select).getByRole('option', { name: 'Mandant 1002 · Südwind AG' })).toBeInTheDocument()
})

test('switches Mandant, drops the open document, and reloads customers', async () => {
  window.history.replaceState(null, '', '/sales/order-1')
  const fetchMock = await bootCognito(tenantA, [
    { tenantId: tenantA, isDefault: true },
    { tenantId: tenantB, isDefault: false },
  ])

  render(<AuthGate><App /></AuthGate>)

  expect(await screen.findByRole('heading', { name: 'SO-1001' })).toBeInTheDocument()
  const select = screen.getByRole('combobox', { name: 'Mandant wechseln' })
  expect(select).toHaveValue(tenantA)
  const firstCustomers = fetchMock.mock.calls.find(([requestUrl]) => String(requestUrl).includes('/customers') && !String(requestUrl).includes('/addresses'))
  expect(new Headers(firstCustomers?.[1]?.headers).get('X-Tenant-Id')).toBe(tenantA)

  fireEvent.change(select, { target: { value: tenantB } })

  await waitFor(() => {
    const switched = fetchMock.mock.calls.some(([requestUrl, requestInit]) =>
      String(requestUrl).includes('/customers')
      && !String(requestUrl).includes('/addresses')
      && new Headers(requestInit?.headers).get('X-Tenant-Id') === tenantB)
    expect(switched).toBe(true)
  })
  expect(window.location.href).toBe(`${window.location.origin}/sales`)
})

test('does not reopen the previous Mandant Stamm document after a switch', async () => {
  const fetchMock = await bootCognito(tenantA, [
    { tenantId: tenantA, isDefault: true },
    { tenantId: tenantB, isDefault: false },
  ])

  render(<AuthGate><App /></AuthGate>)
  await screen.findByRole('heading', { name: 'Auftragsliste' })
  fireEvent.click(screen.getByTitle('Stammdaten'))
  await screen.findByRole('heading', { name: 'Artikel anlegen' })
  fireEvent.click(screen.getByRole('button', { name: 'Bearbeiten' }))
  expect(await screen.findByRole('heading', { name: 'Artikel bearbeiten' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/stamm/articles/article-1')

  fireEvent.click(screen.getByTitle('Vertrieb'))
  expect(await screen.findByRole('heading', { name: 'Auftragsliste' })).toBeInTheDocument()
  fireEvent.change(screen.getByRole('combobox', { name: 'Mandant wechseln' }), { target: { value: tenantB } })
  await waitFor(() => {
    expect(window.location.pathname).toBe('/sales')
  })

  fireEvent.click(screen.getByTitle('Stammdaten'))
  expect(await screen.findByRole('heading', { name: 'Artikel, Adressen & Firma' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/stamm/articles')
  const articleReads = fetchMock.mock.calls.filter(([requestUrl, requestInit]) =>
    String(requestUrl).includes('/articles/article-1')
    && new Headers(requestInit?.headers).get('X-Tenant-Id') === tenantB)
  expect(articleReads).toHaveLength(0)
})

test('puts the open article into the URL', async () => {
  render(<App />)
  fireEvent.click(screen.getByTitle('Stammdaten'))
  await screen.findByRole('heading', { name: 'Artikel anlegen' })

  fireEvent.click(screen.getByRole('button', { name: 'Bearbeiten' }))

  expect(window.location.pathname).toBe('/stamm/articles/article-1')
  expect(screen.getByRole('heading', { name: 'Artikel bearbeiten' })).toBeInTheDocument()
})
