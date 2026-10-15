import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { erpApi } from '../api'
import type { Article, Customer, CustomerAddress, OrderLine, SalesOrder } from '../api'
import { keepOrFirst, nextOrderNumber } from '../format'
import { createId } from '../id'
import type { AppRoute } from '../route'

const initialOrderNumber = `SO-${createId().replaceAll('-', '').slice(0, 8).toUpperCase()}`
type WorkflowStep = 'capture' | 'release' | 'deliver' | 'invoice'

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
  const loadedOrderId = useRef<string | null>(null)

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
    if (!orderId || loadedOrderId.current === orderId) return

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
    () => lines.reduce((total, line) => total + line.quantity * line.unitPrice, 0),
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
        quantity,
        unitPrice: article.salesPrice,
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
      setNotice(`Auftrag ${released.orderNumber} freigegeben. Lieferung und Rechnung erzeugt der Worker im Hintergrund.`)
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
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Storno fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  function startNewOrder() {
    loadedOrderId.current = null
    setOrder(null)
    setLines([])
    setStep('capture')
    setCustomerId('')
    setShipToAddressId('')
    setWarehouseCode('MAIN')
    setOrderNumber((current) => nextOrderNumber(current))
    setNotice('Neuer Auftrag. Position hinzufügen, speichern, dann freigeben.')
    navigate({ workspace: 'sales', documentId: null })
  }

  async function reloadShippingLinks(forCustomerId: string, selectFirstIfEmpty: boolean) {
    if (forCustomerId !== selectedCustomerId) return
    const links = await erpApi.customerAddresses(forCustomerId, 'Shipping')
    setShippingLinks(links)
    if (selectFirstIfEmpty) {
      setShipToAddressId((current) => current || links[0]?.addressId || '')
    }
  }

  const openRoute: AppRoute = { workspace: 'sales', documentId: order?.id ?? null }

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
  }
}

export type SalesModel = ReturnType<typeof useSalesWorkspace>

