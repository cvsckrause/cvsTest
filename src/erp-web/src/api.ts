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
