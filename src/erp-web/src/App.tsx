import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { erpApi } from './api'
import type { Article, Customer, OrderLine, SalesOrder, StockBalanceRow, StockMovementRow } from './api'
import { createId } from './id'

const initialOrderNumber = `SO-${createId().replaceAll('-', '').slice(0, 8).toUpperCase()}`
type WorkflowStep = 'capture' | 'release' | 'deliver' | 'invoice'
type Workspace = 'sales' | 'lager'

function App() {
  const [workspace, setWorkspace] = useState<Workspace>('sales')
  const [customers, setCustomers] = useState<Customer[]>([])
  const [articles, setArticles] = useState<Article[]>([])
  const [stock, setStock] = useState<StockBalanceRow[]>([])
  const [movements, setMovements] = useState<StockMovementRow[]>([])
  const [customerId, setCustomerId] = useState('')
  const [articleId, setArticleId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [receiptArticleId, setReceiptArticleId] = useState('')
  const [receiptQuantity, setReceiptQuantity] = useState(1)
  const [receiptWarehouse, setReceiptWarehouse] = useState('MAIN')
  const [receiptReference, setReceiptReference] = useState('')
  const [orderNumber, setOrderNumber] = useState(initialOrderNumber)
  const [lines, setLines] = useState<OrderLine[]>([])
  const [order, setOrder] = useState<SalesOrder | null>(null)
  const [step, setStep] = useState<WorkflowStep>('capture')
  const [notice, setNotice] = useState('Stammdaten werden geladen …')
  const [busy, setBusy] = useState(false)
  const receiptIdempotencyKey = useRef<string | null>(null)

  useEffect(() => {
    Promise.all([erpApi.customers(), erpApi.articles()])
      .then(([loadedCustomers, loadedArticles]) => {
        setCustomers(loadedCustomers)
        setArticles(loadedArticles)
        setCustomerId(loadedCustomers[0]?.id ?? '')
        setArticleId(loadedArticles[0]?.id ?? '')
        setReceiptArticleId(loadedArticles[0]?.id ?? '')
        setNotice('Bereit')
      })
      .catch((error: Error) => setNotice(`API nicht erreichbar: ${error.message}`))
  }, [])

  useEffect(() => {
    if (workspace !== 'lager') return

    setBusy(true)
    Promise.all([erpApi.stock(), erpApi.stockMovements()])
      .then(([loadedStock, loadedMovements]) => {
        setStock(loadedStock)
        setMovements(loadedMovements)
        setNotice('Lager bereit')
      })
      .catch((error: Error) => setNotice(error.message))
      .finally(() => setBusy(false))
  }, [workspace])

  const netAmount = useMemo(
    () => lines.reduce((total, line) => total + line.quantity * line.unitPrice, 0),
    [lines],
  )

  function openSales() {
    setWorkspace('sales')
    setNotice('Vertrieb')
  }

  function openLager() {
    setWorkspace('lager')
    setNotice('Lager wird geladen …')
  }

  function addLine(event: FormEvent) {
    event.preventDefault()
    const article = articles.find((candidate) => candidate.id === articleId)
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
    if (!customerId || lines.length === 0) {
      setNotice('Kunde und mindestens eine Position sind erforderlich. Klicke „Position hinzufügen“.')
      return
    }
    setBusy(true)
    try {
      const saved = await erpApi.createOrder(customerId, orderNumber, lines)
      setOrder(saved)
      setStep('release')
      setNotice(`Auftrag ${saved.orderNumber} gespeichert. Als Nächstes: „Auftrag freigeben“.`)
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
      setOrder(released)
      setStep('deliver')
      setNotice(`Auftrag ${released.orderNumber} freigegeben. Lieferung und Rechnung erzeugt der Worker im Hintergrund.`)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Freigabe fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  function startNewOrder() {
    setOrder(null)
    setLines([])
    setStep('capture')
    setOrderNumber((current) => current.replace(/(\d+)$/, (digits) => String(Number(digits) + 1).padStart(digits.length, '0')))
    setNotice('Neuer Auftrag. Position hinzufügen, speichern, dann freigeben.')
  }

  async function bookReceipt(event: FormEvent) {
    event.preventDefault()
    if (!receiptArticleId || receiptQuantity <= 0) {
      setNotice('Artikel und positive Menge sind für den Wareneingang erforderlich.')
      return
    }

    if (!receiptIdempotencyKey.current) {
      receiptIdempotencyKey.current = createId()
    }

    setBusy(true)
    try {
      const receipt = await erpApi.createStockReceipt(
        receiptArticleId,
        receiptQuantity,
        receiptWarehouse || 'MAIN',
        receiptReference,
        receiptIdempotencyKey.current,
      )
      // Booking succeeded — never reuse this key for a new intentional receipt.
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

  return (
    <div className="app-shell">
      <aside className="rail">
        <div className="product-mark" aria-label="Northstar ERP">N</div>
        <nav aria-label="Hauptnavigation">
          <button className={`nav-item${workspace === 'sales' ? ' active' : ''}`} title="Vertrieb" onClick={openSales}><span>V</span>Vertrieb</button>
          <button className="nav-item" title="Stammdaten" onClick={() => setNotice('Stammdaten sind in dieser Scheibe nur Kunde und Artikel in der Auftragserfassung.')}><span>S</span>Stamm</button>
          <button className={`nav-item${workspace === 'lager' ? ' active' : ''}`} title="Lager" onClick={openLager}><span>L</span>Lager</button>
          <button className="nav-item" title="Auswertungen" onClick={() => setNotice('Auswertungen sind in dieser Scheibe nicht enthalten.')}><span>A</span>Analyse</button>
        </nav>
        <div className="rail-footer">CK</div>
      </aside>

      <main className={workspace === 'lager' ? 'lager-main' : undefined}>
        {workspace === 'sales' ? (
          <>
            <header className="topbar">
              <div>
                <p className="breadcrumb">Vertrieb / Auftragserfassung</p>
                <h1>Neuer Auftrag</h1>
              </div>
              <div className="system-state"><span /> Entwicklung · Nordics</div>
            </header>

            <div className="workflow" aria-label="Auftragsfortschritt">
              <button type="button" className={stepClass('capture', step, order)} onClick={() => goTo('capture')}><span>1</span>Erfassen</button>
              <button type="button" className={stepClass('release', step, order)} onClick={() => goTo('release')}><span>2</span>Freigeben</button>
              <button type="button" className={stepClass('deliver', step, order)} onClick={() => goTo('deliver')}><span>3</span>Liefern</button>
              <button type="button" className={stepClass('invoice', step, order)} onClick={() => goTo('invoice')}><span>4</span>Rechnen</button>
            </div>

            <section className="workspace">
              <div className="document">
                <div className="document-head">
                  <label>
                    Auftragsnummer
                    <input value={orderNumber} onChange={(event) => setOrderNumber(event.target.value)} disabled={Boolean(order)} />
                  </label>
                  <label className="customer-field">
                    Kunde
                    <select value={customerId} onChange={(event) => setCustomerId(event.target.value)} disabled={Boolean(order)}>
                      <option value="">Kunde wählen</option>
                      {customers.map((customer) => (
                        <option key={customer.id} value={customer.id}>{customer.number} · {customer.name}</option>
                      ))}
                    </select>
                  </label>
                </div>

                <div className="section-title">
                  <h2>Positionen</h2>
                  <span>{lines.length} Positionen</span>
                </div>

                <div className="line-grid" role="table" aria-label="Auftragspositionen">
                  <div className="grid-row grid-head" role="row">
                    <span>Pos.</span><span>Artikel</span><span>Menge</span><span>Preis</span><span>Betrag</span><span />
                  </div>
                  {lines.map((line, index) => (
                    <div className="grid-row" role="row" key={`${line.articleId}-${index}`}>
                      <span>{String(index + 1).padStart(2, '0')}</span>
                      <strong>{line.sku}</strong>
                      <span>{line.quantity.toLocaleString('de-DE')}</span>
                      <span>{euro(line.unitPrice)}</span>
                      <strong>{euro(line.quantity * line.unitPrice)}</strong>
                      <button className="remove" onClick={() => setLines((current) => current.filter((_, lineIndex) => lineIndex !== index))} disabled={Boolean(order)} aria-label={`Position ${index + 1} entfernen`}>×</button>
                    </div>
                  ))}
                  {lines.length === 0 && <div className="empty-row">Noch keine Position. Artikel unten hinzufügen.</div>}
                </div>

                <form className="add-line" onSubmit={addLine}>
                  <label>
                    Artikel
                    <select value={articleId} onChange={(event) => setArticleId(event.target.value)} disabled={Boolean(order)}>
                      <option value="">Artikel wählen</option>
                      {articles.map((article) => (
                        <option key={article.id} value={article.id}>{article.sku} · {article.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Menge
                    <input type="number" min="0.01" step="0.01" value={quantity} onChange={(event) => setQuantity(Number(event.target.value))} disabled={Boolean(order)} />
                  </label>
                  <button className="secondary" type="submit" disabled={Boolean(order)}>Position hinzufügen</button>
                </form>
              </div>

              <aside className="summary">
                <div className="summary-heading">
                  <span className={`status ${order?.status === 'Released' ? 'released' : ''}`}>{order?.status === 'Released' ? 'Freigegeben' : order ? 'Gespeichert' : 'Entwurf'}</span>
                  <span>{orderNumber}</span>
                </div>
                <dl>
                  <div><dt>Warenwert</dt><dd>{euro(order?.netAmount ?? netAmount)}</dd></div>
                  <div><dt>Rabatt</dt><dd>0,00 €</dd></div>
                  <div className="total"><dt>Nettobetrag</dt><dd>{euro(order?.netAmount ?? netAmount)}</dd></div>
                </dl>
                <div className="summary-actions">
                  <p className="action-hint">{notice}</p>
                  {step === 'capture' && !order && <button className="primary" onClick={saveOrder} disabled={busy}>{busy ? 'Speichert …' : 'Auftrag speichern'}</button>}
                  {(step === 'release' || (order && order.status !== 'Released')) && order?.status !== 'Released' && (
                    <button className="primary" onClick={releaseOrder} disabled={busy || !order}>
                      {order ? 'Auftrag freigeben' : 'Zuerst speichern'}
                    </button>
                  )}
                  {order?.status === 'Released' && (
                    <>
                      <p className="released-note">Bestand reserviert. Lieferung und Rechnung erzeugt der Worker im Hintergrund — in dieser Scheibe gibt es keine weiteren Masken.</p>
                      <button className="secondary" type="button" onClick={startNewOrder}>Neuen Auftrag erfassen</button>
                    </>
                  )}
                </div>
                <div className="shortcuts">
                  <p>Tastatur</p>
                  <span><kbd>Tab</kbd> nächstes Feld</span>
                  <span><kbd>Enter</kbd> Position hinzufügen</span>
                </div>
              </aside>
            </section>
          </>
        ) : (
          <>
            <header className="topbar">
              <div>
                <p className="breadcrumb">Lager / Bestand</p>
                <h1>Bestand &amp; Wareneingang</h1>
              </div>
              <div className="system-state"><span /> Entwicklung · Nordics</div>
            </header>

            <section className="workspace lager-workspace">
              <div className="document">
                <div className="section-title">
                  <h2>Bestandübersicht</h2>
                  <span>{stock.length} Positionen</span>
                </div>
                <div className="line-grid" role="table" aria-label="Bestandübersicht">
                  <div className="grid-row grid-head stock-row" role="row">
                    <span>Artikel</span><span>Lager</span><span>Verfügbar</span><span>Reserviert</span><span>Bestand</span>
                  </div>
                  {stock.map((row) => (
                    <div className="grid-row stock-row" role="row" key={`${row.articleId}-${row.warehouseCode}`}>
                      <strong>{row.sku} · {row.name}</strong>
                      <span>{row.warehouseCode}</span>
                      <span>{qty(row.available)}</span>
                      <span>{qty(row.reserved)}</span>
                      <span>{qty(row.onHand)}</span>
                    </div>
                  ))}
                  {stock.length === 0 && <div className="empty-row">Kein Bestand vorhanden.</div>}
                </div>

                <div className="section-title">
                  <h2>Bewegungen</h2>
                  <span>{movements.length} Einträge</span>
                </div>
                <div className="line-grid" role="table" aria-label="Lagerbewegungen">
                  <div className="grid-row grid-head movement-row" role="row">
                    <span>Zeit</span><span>Typ</span><span>Artikel</span><span>Lager</span><span>Menge</span><span>Referenz</span>
                  </div>
                  {movements.map((movement) => (
                    <div className="grid-row movement-row" role="row" key={movement.id}>
                      <span>{formatWhen(movement.occurredAt)}</span>
                      <span>{movement.movementType === 'GoodsReceipt' ? 'Wareneingang' : movement.movementType}</span>
                      <strong>{movement.sku}</strong>
                      <span>{movement.warehouseCode}</span>
                      <span>{qty(movement.quantity)}</span>
                      <span>{movement.reference ?? '—'}</span>
                    </div>
                  ))}
                  {movements.length === 0 && <div className="empty-row">Noch keine Bewegungen.</div>}
                </div>
              </div>

              <aside className="summary">
                <div className="summary-heading">
                  <span className="status">Wareneingang</span>
                  <span>{receiptWarehouse || 'MAIN'}</span>
                </div>
                <form className="receipt-form" onSubmit={bookReceipt}>
                  <label>
                    Artikel
                    <select value={receiptArticleId} onChange={(event) => setReceiptArticleId(event.target.value)}>
                      <option value="">Artikel wählen</option>
                      {articles.map((article) => (
                        <option key={article.id} value={article.id}>{article.sku} · {article.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Lager
                    <input value={receiptWarehouse} onChange={(event) => setReceiptWarehouse(event.target.value.toUpperCase())} />
                  </label>
                  <label>
                    Menge
                    <input type="number" min="0.01" step="0.01" value={receiptQuantity} onChange={(event) => setReceiptQuantity(Number(event.target.value))} />
                  </label>
                  <label>
                    Referenz
                    <input value={receiptReference} onChange={(event) => setReceiptReference(event.target.value)} placeholder="optional" />
                  </label>
                  <button className="primary" type="submit" disabled={busy}>{busy ? 'Bucht …' : 'Wareneingang buchen'}</button>
                </form>
                <div className="summary-actions">
                  <p className="action-hint">{notice}</p>
                </div>
              </aside>
            </section>
          </>
        )}

        <footer className="statusbar" aria-live="polite">
          <span>{notice}</span>
          <span>Mandant 100 · Hauptlager MAIN</span>
        </footer>
      </main>
    </div>
  )
}

function stepClass(id: WorkflowStep, current: WorkflowStep, order: SalesOrder | null) {
  const released = order?.status === 'Released'
  const done = (id === 'capture' && Boolean(order))
    || (id === 'release' && released)
    || ((id === 'deliver' || id === 'invoice') && released && current !== id)
  if (current === id) return 'workflow-step current'
  if (done) return 'workflow-step done'
  return 'workflow-step'
}

function euro(value: number) {
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' }).format(value)
}

function qty(value: number) {
  return new Intl.NumberFormat('de-DE', { maximumFractionDigits: 4 }).format(value)
}

function formatWhen(value: string) {
  return new Intl.DateTimeFormat('de-DE', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}

export default App
