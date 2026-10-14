import { createId } from './id'

export type Customer = {
  id: string
  number: string
  name: string
}

export type Address = {
  id: string
  name: string
  street: string
  postalCode: string
  city: string
  countryCode: string
  additionalLine?: string | null
}

export type CustomerAddress = {
  id: string
  customerId: string
  addressId: string
  role: string
  address: Address
}

export type Article = {
  id: string
  sku: string
  name: string
  salesPrice: number
}

export type OrderLine = {
  articleId: string
  sku: string
  quantity: number
  unitPrice: number
}

export type SalesOrder = {
  id: string
  orderNumber: string
  customerId: string
  shipToAddressId: string
  shipTo: Address
  status: string
  netAmount: number
  lines: OrderLine[]
}

export type StockBalanceRow = {
  articleId: string
  sku: string
  name: string
  warehouseCode: string
  onHand: number
  reserved: number
  available: number
}

export type StockMovementRow = {
  id: string
  articleId: string
  sku: string
  name: string
  warehouseCode: string
  quantity: number
  movementType: string
  reference: string | null
  createdBy: string
  occurredAt: string
}

export type StockReceipt = {
  id: string
  articleId: string
  sku: string
  warehouseCode: string
  quantity: number
  reference: string | null
  occurredAt: string
  replayed: boolean
}

const baseUrl = import.meta.env.VITE_API_URL ?? '/api/v1'

async function readJson<T>(response: Response, path: string): Promise<T> {
  const contentType = response.headers.get('content-type') ?? ''
  const raw = await response.text()
  if (!raw) {
    if (!response.ok) throw new Error(`Speichern fehlgeschlagen (${response.status})`)
    return undefined as T
  }

  try {
    return JSON.parse(raw) as T
  } catch {
    if (contentType.includes('text/html') || raw.trimStart().startsWith('<!')) {
      throw new Error(
        `API ${path} lieferte HTML statt JSON (${response.status}). ` +
          'Wahrscheinlich ist die AWS-Zelle noch auf dem alten Image — neu bauen/deployen, oder lokal die neue API starten.',
      )
    }
    throw new Error(`Ungültige API-Antwort für ${path} (${response.status}).`)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...init?.headers,
      },
    })
  } catch {
    throw new Error('API nicht erreichbar. Seite neu laden oder prüfen, ob die ERP-API läuft.')
  }

  const payload = await readJson<{ error?: string } & T>(response, path)
  if (!response.ok) {
    throw new Error(payload?.error ?? `Speichern fehlgeschlagen (${response.status})`)
  }

  return payload as T
}

export const erpApi = {
  customers: () => request<Customer[]>('/customers'),
  articles: (q?: string) => request<Article[]>(`/articles${q ? `?q=${encodeURIComponent(q)}` : ''}`),
  createArticle: (sku: string, name: string, salesPrice: number, openingStock = 0) =>
    request<Article>('/articles', {
      method: 'POST',
      body: JSON.stringify({ sku, name, salesPrice, openingStock }),
    }),
  updateArticle: (id: string, name: string, salesPrice: number) =>
    request<Article>(`/articles/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, salesPrice }),
    }),
  addresses: (q?: string) => request<Address[]>(`/addresses${q ? `?q=${encodeURIComponent(q)}` : ''}`),
  createAddress: (payload: Omit<Address, 'id'>) =>
    request<Address>('/addresses', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateAddress: (id: string, payload: Omit<Address, 'id'>) =>
    request<Address>(`/addresses/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  customerAddresses: (customerId: string, role?: string) =>
    request<CustomerAddress[]>(
      `/customers/${customerId}/addresses${role ? `?role=${encodeURIComponent(role)}` : ''}`,
    ),
  linkCustomerAddress: (customerId: string, addressId: string, role: string) =>
    request<CustomerAddress>(`/customers/${customerId}/addresses`, {
      method: 'POST',
      body: JSON.stringify({ addressId, role }),
    }),
  stock: () => request<StockBalanceRow[]>('/stock'),
  stockMovements: (limit = 50) => request<StockMovementRow[]>(`/stock/movements?limit=${limit}`),
  createStockReceipt: (
    articleId: string,
    quantity: number,
    warehouseCode: string,
    reference: string | undefined,
    idempotencyKey: string,
  ) =>
    request<StockReceipt>('/stock/receipts', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({
        articleId,
        quantity,
        warehouseCode,
        reference: reference?.trim() ? reference.trim() : null,
      }),
    }),
  createOrder: (customerId: string, shipToAddressId: string, orderNumber: string, lines: OrderLine[]) =>
    request<SalesOrder>('/sales-orders', {
      method: 'POST',
      headers: { 'Idempotency-Key': createId() },
      body: JSON.stringify({
        customerId,
        shipToAddressId,
        orderNumber,
        lines: lines.map((line) => ({ articleId: line.articleId, quantity: line.quantity })),
      }),
    }),
  releaseOrder: (id: string) =>
    request<SalesOrder>(`/sales-orders/${id}/release`, {
      method: 'POST',
      headers: { 'Idempotency-Key': createId() },
    }),
}
