import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { erpApi } from '../api'
import type { Article, StockBalanceRow, StockMovementRow } from '../api'
import { keepOrFirst } from '../format'
import { createId } from '../id'

type LagerModelArgs = {
  active: boolean
  articles: Article[]
  notice: string
  setNotice: (notice: string) => void
  busy: boolean
  setBusy: (busy: boolean) => void
}

export function useLagerWorkspace({ active, articles, notice, setNotice, busy, setBusy }: LagerModelArgs) {
  const [stock, setStock] = useState<StockBalanceRow[]>([])
  const [movements, setMovements] = useState<StockMovementRow[]>([])
  const [receiptArticleId, setReceiptArticleId] = useState('')
  const [receiptQuantity, setReceiptQuantity] = useState(1)
  const [receiptWarehouse, setReceiptWarehouse] = useState('MAIN')
  const [receiptReference, setReceiptReference] = useState('')
  const receiptIdempotencyKey = useRef<string | null>(null)
  const selectedReceiptArticleId = keepOrFirst(receiptArticleId, articles.map((article) => article.id))

  useEffect(() => {
    if (!active) return

    let cancelled = false
    Promise.all([erpApi.stock(), erpApi.stockMovements()])
      .then(([loadedStock, loadedMovements]) => {
        if (cancelled) return
        setStock(loadedStock)
        setMovements(loadedMovements)
        setNotice('Lager bereit')
      })
      .catch((error: Error) => {
        if (!cancelled) setNotice(error.message)
      })
      .finally(() => {
        if (!cancelled) setBusy(false)
      })

    return () => {
      cancelled = true
    }
  }, [active, setBusy, setNotice])

  async function bookReceipt(event: FormEvent) {
    event.preventDefault()
    if (!selectedReceiptArticleId || receiptQuantity <= 0) {
      setNotice('Artikel und positive Menge sind für den Wareneingang erforderlich.')
      return
    }

    if (!receiptIdempotencyKey.current) {
      receiptIdempotencyKey.current = createId()
    }

    setBusy(true)
    try {
      const receipt = await erpApi.createStockReceipt(
        selectedReceiptArticleId,
        receiptQuantity,
        receiptWarehouse || 'MAIN',
        receiptReference,
        receiptIdempotencyKey.current,
      )
      receiptIdempotencyKey.current = null
      const bookedNotice = receipt.replayed
        ? `Wareneingang ${receipt.sku} bereits gebucht (Idempotenz).`
        : `Wareneingang ${receipt.quantity.toLocaleString('de-DE')} × ${receipt.sku} gebucht.`

      try {
        const [loadedStock, loadedMovements] = await Promise.all([erpApi.stock(), erpApi.stockMovements()])
        setStock(loadedStock)
        setMovements(loadedMovements)
        setReceiptQuantity(1)
        setReceiptReference('')
        setNotice(bookedNotice)
      } catch (refreshError) {
        setNotice(
          `${bookedNotice} Anzeige konnte nicht aktualisiert werden: ${
            refreshError instanceof Error ? refreshError.message : 'unbekannter Fehler'
          }`,
        )
      }
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Wareneingang fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  return {
    notice,
    busy,
    articles,
    stock,
    movements,
    selectedReceiptArticleId,
    setReceiptArticleId,
    receiptQuantity,
    setReceiptQuantity,
    receiptWarehouse,
    setReceiptWarehouse,
    receiptReference,
    setReceiptReference,
    bookReceipt,
  }
}

export type LagerModel = ReturnType<typeof useLagerWorkspace>

