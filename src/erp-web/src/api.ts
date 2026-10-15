import { authHeaders } from './auth/runtime'
import { createId } from './id'
import type { components } from './api.generated'

type Schemas = components['schemas']

type JsonNumber<T> = [number] extends [T] ? ([string] extends [T] ? number : T) : T
type Json<T> = [T] extends [readonly (infer Item)[]]
  ? Json<Item>[]
  : [T] extends [object]
    ? { [Key in keyof T]: Json<T[Key]> }
    : JsonNumber<T>

export type Customer = Json<Schemas['CustomerResponse']>
export type Address = Json<Schemas['AddressResponse']>
export type CustomerAddress = Json<Schemas['CustomerAddressResponse']>
export type Article = Json<Schemas['ArticleResponse']>
export type OrderLine = Json<Schemas['SalesOrderLineResponse']>
export type SalesOrder = Json<Schemas['SalesOrderResponse']>
export type StockBalanceRow = Json<Schemas['StockResponse']>
export type StockMovementRow = Json<Schemas['StockMovementResponse']>
export type StockReceipt = Json<Schemas['StockReceiptResponse']>

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
    const authorization = await authHeaders()
    response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...authorization,
        ...init?.headers,
      },
    })
  } catch {
    throw new Error('API nicht erreichbar. Seite neu laden oder prüfen, ob die ERP-API läuft.')
  }

  const payload = await readJson<{ detail?: string; error?: string } & T>(response, path)
  if (!response.ok) {
    throw new Error(payload?.detail ?? payload?.error ?? `Speichern fehlgeschlagen (${response.status})`)
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
  salesOrder: (id: string) => request<SalesOrder>(`/sales-orders/${id}`),
  article: (id: string) => request<Article>(`/articles/${id}`),
  address: (id: string) => request<Address>(`/addresses/${id}`),
}
