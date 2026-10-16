import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { erpApi } from '../api'
import type {
  Article,
  Customer,
  CustomerAddress,
  Delivery,
  Invoice,
  OrderLine,
  SalesOrder,
  SalesOrderSummary,
} from '../api'
import { keepOrFirst, roundCents } from '../format'
import type { AppRoute } from '../route'

const initialOrderNumber = ''
const POLL_MS = 2000
const POLL_MAX_ATTEMPTS = 30
type WorkflowStep = 'capture' | 'release' | 'deliver' | 'invoice'
export type OrderListStatusFilter = '' | 'Draft' | 'Released' | 'Cancelled'

type SalesModelArgs = {
  orderId: string | null
  customers: Customer[]
  articles: Article[]
  notice: string
  setNotice: (notice: string) => void
  busy: boolean
  setBusy: (busy: boolean) => void
  navigate: (route: AppRoute, mode?: 'push' | 'replace') => void
}

export function useSalesWorkspace({
  orderId,
  customers,
  articles,
  notice,
  setNotice,
  busy,
  setBusy,
  navigate,
}: SalesModelArgs) {
  const [customerId, setCustomerId] = useState('')
  const [shipToAddressId, setShipToAddressId] = useState('')
  const [shippingLinks, setShippingLinks] = useState<CustomerAddress[]>([])
  const [articleId, setArticleId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [orderNumber, setOrderNumber] = useState(initialOrderNumber)
  const [warehouseCode, setWarehouseCode] = useState('MAIN')
  const [lines, setLines] = useState<OrderLine[]>([])
  const [order, setOrder] = useState<SalesOrder | null>(null)
  const [step, setStep] = useState<WorkflowStep>('capture')
  const [orders, setOrders] = useState<SalesOrderSummary[]>([])
  const [listStatus, setListStatus] = useState<OrderListStatusFilter>('')
  const [listBusy, setListBusy] = useState(false)
  const [deliveries, setDeliveries] = useState<Delivery[]>([])
  const [invoices, setInvoices] = useState<Invoice[]>([])
  const [documentsWaiting, setDocumentsWaiting] = useState(false)
  const loadedOrderId = useRef<string | null>(null)
  const listRequest = useRef(0)

  async function refreshOrderList(preferredStatus: OrderListStatusFilter = listStatus) {
    const requestId = ++listRequest.current
    setListBusy(true)
    try {
      const page = await erpApi.salesOrders(preferredStatus || undefined)
      if (requestId === listRequest.current) setOrders(page)
    } catch (error) {
      if (requestId === listRequest.current) {
        setNotice(error instanceof Error ? error.message : 'Auftragsliste nicht ladbar.')
      }
    } finally {
      if (requestId === listRequest.current) setListBusy(false)
    }
  }

  useEffect(() => {
    void refreshOrderList(listStatus)
  }, [listStatus])

  const [pinnedCustomer, setPinnedCustomer] = useState<Customer | null>(null)
  const customerChoices = useMemo(() => {
    if (!customerId || customers.some((customer) => customer.id === customerId)) return customers
    const known = pinnedCustomer?.id === customerId
      ? pinnedCustomer
      : { id: customerId, number: '…', name: 'Kunde wird geladen…' }
    return [...customers, known]
  }, [customerId, customers, pinnedCustomer])
  const selectedCustomerId = keepOrFirst(customerId, customerChoices.map((customer) => customer.id))
  const selectedArticleId = keepOrFirst(articleId, articles.map((article) => article.id))
  const selectedShipToId = shippingLinks.some((link) => link.addressId === shipToAddressId)
    ? shipToAddressId
    : (shippingLinks[0]?.addressId ?? '')

  useEffect(() => {
    if (!orderId || orderId === 'new') {
      const enteringNew = orderId === 'new' && loadedOrderId.current !== 'new'
      loadedOrderId.current = orderId === 'new' ? 'new' : null
      if (!orderId || enteringNew) {
        setOrder(null)
        setDeliveries([])
        setInvoices([])
        setDocumentsWaiting(false)
      }
      if (enteringNew) {
        setLines([])
        setStep('capture')
        setCustomerId('')
        setShipToAddressId('')
        setOrderNumber('')
      }
      return
    }
    if (loadedOrderId.current === orderId) return

    let cancelled = false
    erpApi.salesOrder(orderId)
      .then((loaded) => {
        if (cancelled) return
        loadedOrderId.current = orderId
        setOrder(loaded)
        setLines(loaded.lines)
        setOrderNumber(loaded.orderNumber)
        setWarehouseCode(loaded.warehouseCode)
        setCustomerId(loaded.customerId)
        setShipToAddressId(loaded.shipToAddressId)
        setStep(loaded.status === 'Released' ? 'deliver' : 'capture')
        setNotice(`Auftrag ${loaded.orderNumber} geöffnet.`)
      })
      .catch((error: Error) => {
        if (cancelled) return
        loadedOrderId.current = null
        setNotice(error.message)
      })

    return () => {
      cancelled = true
    }
  }, [orderId, setNotice])

  useEffect(() => {
    if (!order || order.status !== 'Released') {
      setDeliveries([])
      setInvoices([])
      setDocumentsWaiting(false)
      return
    }

    const releasedOrderId = order.id
    let cancelled = false
    let attempts = 0
    let timer: ReturnType<typeof setTimeout> | undefined

    async function loadDocuments() {
      attempts += 1
      try {
        const [nextDeliveries, nextInvoices] = await Promise.all([
          erpApi.salesOrderDeliveries(releasedOrderId),
          erpApi.salesOrderInvoices(releasedOrderId),
        ])
        if (cancelled) return
        setDeliveries(nextDeliveries)
        setInvoices(nextInvoices)
        const ready = nextDeliveries.length > 0 && nextInvoices.length > 0
        setDocumentsWaiting(!ready)
        if (ready) {
          setNotice(`Lieferung ${nextDeliveries[0].number} und Rechnung ${nextInvoices[0].number} liegen vor.`)
          return
        }
        if (attempts < POLL_MAX_ATTEMPTS) {
          timer = setTimeout(() => {
            void loadDocuments()
          }, POLL_MS)
        } else {
          setDocumentsWaiting(false)
          setNotice('Lieferung und Rechnung werden noch erzeugt. Später erneut öffnen oder Worker prüfen.')
        }
      } catch (error) {
        if (cancelled) return
        setDocumentsWaiting(false)
        setNotice(error instanceof Error ? error.message : 'Belege nicht ladbar.')
      }
    }

    setDocumentsWaiting(true)
    void loadDocuments()

    return () => {
      cancelled = true
      if (timer) clearTimeout(timer)
    }
  }, [order, setNotice])

  useEffect(() => {
    if (!customerId || customers.some((customer) => customer.id === customerId)) return
    if (pinnedCustomer?.id === customerId) return

    let cancelled = false
    let after: string | undefined
    const load = async () => {
      for (let page = 0; page < 20; page += 1) {
        const batch = await erpApi.customers(after)
        const found = batch.find((customer) => customer.id === customerId)
        if (found) return found
        const cursor = batch.at(-1)?.number
        if (batch.length < 50 || !cursor || cursor === after) return null
        after = cursor
      }
      return null
    }

    load()
      .then((found) => {
        if (!cancelled && found) setPinnedCustomer(found)
      })
      .catch((error: Error) => {
        if (!cancelled) setNotice(`Kunde nicht ladbar: ${error.message}`)
      })

    return () => {
      cancelled = true
    }
  }, [customerId, customers, pinnedCustomer, setNotice])

  useEffect(() => {
    if (!selectedCustomerId) return

    let cancelled = false
    erpApi.customerAddresses(selectedCustomerId, 'Shipping')
      .then((links) => {
        if (!cancelled) setShippingLinks(links)
      })
      .catch((error: Error) => {
        if (!cancelled) setNotice(`Lieferadressen nicht ladbar: ${error.message}`)
      })

    return () => {
      cancelled = true
    }
  }, [selectedCustomerId, setNotice])

  const netAmount = useMemo(
    () => roundCents(lines.reduce((total, line) => total + line.netAmount, 0)),
    [lines],
  )

  function addLine(event: FormEvent) {
    event.preventDefault()
    const article = articles.find((candidate) => candidate.id === selectedArticleId)
    if (!article || quantity <= 0) return

    setLines((current) => [
      ...current,
      {
        articleId: article.id,
        sku: article.sku,
        description: article.name,
        taxCode: article.taxCode,
        quantity,
        unitPrice: article.salesPrice,
        netAmount: roundCents(quantity * article.salesPrice),
      },
    ])
    setQuantity(1)
  }

  function goTo(next: WorkflowStep) {
    if (next === 'release' && !order) {
      setNotice('Zuerst unten „Position hinzufügen“, dann rechts „Auftrag speichern“. Danach kannst du freigeben.')
      return
    }
    if ((next === 'deliver' || next === 'invoice') && order?.status !== 'Released') {
      setNotice('Zuerst den Auftrag speichern und freigeben. Lieferung und Rechnung kommen danach automatisch.')
      return
    }
    setStep(next)
  }

  async function saveOrder() {
    if (!selectedCustomerId || !selectedShipToId || lines.length === 0) {
      setNotice('Kunde, Lieferadresse und mindestens eine Position sind erforderlich.')
      return
    }
    setBusy(true)
    try {
      const warehouse = warehouseCode.trim().toUpperCase() || 'MAIN'
      const saved = await erpApi.createOrder(selectedCustomerId, selectedShipToId, orderNumber, lines, warehouse)
      loadedOrderId.current = saved.id
      setOrder(saved)
      setWarehouseCode(saved.warehouseCode)
      setStep('release')
      setNotice(`Auftrag ${saved.orderNumber} gespeichert. Als Nächstes: „Auftrag freigeben“.`)
      navigate({ workspace: 'sales', documentId: saved.id }, 'replace')
      void refreshOrderList()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Auftrag konnte nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  async function releaseOrder() {
    if (!order) {
      goTo('release')
      return
    }
    setBusy(true)
    try {
      const released = await erpApi.releaseOrder(order.id)
      loadedOrderId.current = released.id
      setOrder(released)
      setStep('deliver')
      setNotice(`Auftrag ${released.orderNumber} freigegeben. Warte auf Lieferung und Rechnung …`)
      void refreshOrderList()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Freigabe fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  async function cancelOrder() {
    if (!order || order.status === 'Cancelled') return
    setBusy(true)
    try {
      const cancelled = await erpApi.cancelOrder(order.id)
      loadedOrderId.current = cancelled.id
      setOrder(cancelled)
      setWarehouseCode(cancelled.warehouseCode)
      setStep('capture')
      setNotice('Der Auftrag ist storniert. Eine Lieferung wird nicht erzeugt.')
      void refreshOrderList()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Storno fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  function openOrderFromList(id: string) {
    navigate({ workspace: 'sales', documentId: id })
  }

  function showOrderList() {
    loadedOrderId.current = null
    setOrder(null)
    setDeliveries([])
    setInvoices([])
    setDocumentsWaiting(false)
    navigate({ workspace: 'sales', documentId: null })
    void refreshOrderList()
  }

  function startNewOrder() {
    loadedOrderId.current = 'new'
    setOrder(null)
    setDeliveries([])
    setInvoices([])
    setDocumentsWaiting(false)
    setLines([])
    setStep('capture')
    setCustomerId('')
    setShipToAddressId('')
    setWarehouseCode('MAIN')
    setOrderNumber('')
    setNotice('Neuer Auftrag. Position hinzufügen, speichern, dann freigeben.')
    navigate({ workspace: 'sales', documentId: 'new' })
  }

  async function reloadShippingLinks(forCustomerId: string, selectFirstIfEmpty: boolean) {
    if (forCustomerId !== selectedCustomerId) return
    const links = await erpApi.customerAddresses(forCustomerId, 'Shipping')
    setShippingLinks(links)
    if (selectFirstIfEmpty) {
      setShipToAddressId((current) => current || links[0]?.addressId || '')
    }
  }

  const openRoute: AppRoute = {
    workspace: 'sales',
    documentId: order?.id ?? (orderId === 'new' ? 'new' : null),
  }
  const showingList = !orderId

  return {
    notice,
    busy,
    order,
    step,
    orderNumber,
    setOrderNumber,
    warehouseCode,
    setWarehouseCode,
    customers: customerChoices,
    articles,
    shippingLinks,
    lines,
    setLines,
    netAmount,
    selectedCustomerId,
    selectedArticleId,
    selectedShipToId,
    quantity,
    setQuantity,
    setCustomerId,
    setArticleId,
    setShipToAddressId,
    setShippingLinks,
    addLine,
    goTo,
    saveOrder,
    releaseOrder,
    cancelOrder,
    startNewOrder,
    reloadShippingLinks,
    openRoute,
    showingList,
    orders,
    listStatus,
    setListStatus,
    listBusy,
    openOrderFromList,
    showOrderList,
    deliveries,
    invoices,
    documentsWaiting,
  }
}

export type SalesModel = ReturnType<typeof useSalesWorkspace>

