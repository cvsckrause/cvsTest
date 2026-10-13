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

const baseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5080/api/v1'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => ({ error: response.statusText }))
    throw new Error(problem.error ?? `Request failed (${response.status})`)
  }

  return response.json() as Promise<T>
}

export const erpApi = {
  customers: () => request<Customer[]>('/customers'),
  articles: () => request<Article[]>('/articles'),
  createOrder: (customerId: string, orderNumber: string, lines: OrderLine[]) =>
    request<SalesOrder>('/sales-orders', {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({
        customerId,
        orderNumber,
        lines: lines.map((line) => ({ articleId: line.articleId, quantity: line.quantity })),
      }),
    }),
  releaseOrder: (id: string) =>
    request<SalesOrder>(`/sales-orders/${id}/release`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    }),
}
