import { createId } from './id'

export type Customer = {
  id: string
  number: string
  name: string
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

  if (!response.ok) {
    const problem = await response.json().catch(() => ({ error: `${response.status} ${response.statusText}` }))
    throw new Error(problem.error ?? `Speichern fehlgeschlagen (${response.status})`)
  }

  return response.json() as Promise<T>
}

export const erpApi = {
  customers: () => request<Customer[]>('/customers'),
  articles: () => request<Article[]>('/articles'),
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
  createOrder: (customerId: string, orderNumber: string, lines: OrderLine[]) =>
    request<SalesOrder>('/sales-orders', {
      method: 'POST',
      headers: { 'Idempotency-Key': createId() },
      body: JSON.stringify({
        customerId,
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
