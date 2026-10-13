import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { erpApi } from './api'
import type { Article, Customer, OrderLine, SalesOrder } from './api'

const initialOrderNumber = 'SO-2026-0001'

function App() {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [articles, setArticles] = useState<Article[]>([])
  const [customerId, setCustomerId] = useState('')
  const [articleId, setArticleId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [orderNumber, setOrderNumber] = useState(initialOrderNumber)
  const [lines, setLines] = useState<OrderLine[]>([])
  const [order, setOrder] = useState<SalesOrder | null>(null)
  const [notice, setNotice] = useState('Stammdaten werden geladen …')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    Promise.all([erpApi.customers(), erpApi.articles()])
      .then(([loadedCustomers, loadedArticles]) => {
        setCustomers(loadedCustomers)
        setArticles(loadedArticles)
        setCustomerId(loadedCustomers[0]?.id ?? '')
        setArticleId(loadedArticles[0]?.id ?? '')
        setNotice('Bereit')
      })
      .catch((error: Error) => setNotice(`API nicht erreichbar: ${error.message}`))
  }, [])

  const netAmount = useMemo(
    () => lines.reduce((total, line) => total + line.quantity * line.unitPrice, 0),
    [lines],
  )

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

  async function saveOrder() {
    if (!customerId || lines.length === 0) {
      setNotice('Kunde und mindestens eine Position sind erforderlich.')
      return
    }
    setBusy(true)
    try {
      const saved = await erpApi.createOrder(customerId, orderNumber, lines)
      setOrder(saved)
      setNotice(`Auftrag ${saved.orderNumber} gespeichert.`)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Auftrag konnte nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  async function releaseOrder() {
    if (!order) return
    setBusy(true)
    try {
      const released = await erpApi.releaseOrder(order.id)
      setOrder(released)
      setNotice(`Auftrag ${released.orderNumber} freigegeben und Bestand reserviert.`)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Freigabe fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="app-shell">
      <aside className="rail">
        <div className="product-mark" aria-label="Northstar ERP">N</div>
        <nav aria-label="Hauptnavigation">
          <button className="nav-item active" title="Vertrieb"><span>V</span>Vertrieb</button>
          <button className="nav-item" title="Stammdaten"><span>S</span>Stamm</button>
          <button className="nav-item" title="Lager"><span>L</span>Lager</button>
          <button className="nav-item" title="Auswertungen"><span>A</span>Analyse</button>
        </nav>
        <div className="rail-footer">CK</div>
      </aside>

      <main>
        <header className="topbar">
          <div>
            <p className="breadcrumb">Vertrieb / Auftragserfassung</p>
            <h1>Neuer Auftrag</h1>
          </div>
          <div className="system-state"><span /> Entwicklung · Nordics</div>
        </header>

        <div className="workflow" aria-label="Auftragsfortschritt">
          <div className="workflow-step current"><span>1</span>Erfassen</div>
          <div className={order?.status === 'Released' ? 'workflow-step done' : 'workflow-step'}><span>2</span>Freigeben</div>
          <div className="workflow-step"><span>3</span>Liefern</div>
          <div className="workflow-step"><span>4</span>Rechnen</div>
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
              {!order && <button className="primary" onClick={saveOrder} disabled={busy}>Auftrag speichern</button>}
              {order && order.status !== 'Released' && <button className="primary" onClick={releaseOrder} disabled={busy}>Auftrag freigeben</button>}
              {order?.status === 'Released' && <p className="released-note">Bestand reserviert. Lieferung und Rechnung werden im Hintergrund erzeugt.</p>}
            </div>
            <div className="shortcuts">
              <p>Tastatur</p>
              <span><kbd>Tab</kbd> nächstes Feld</span>
              <span><kbd>Enter</kbd> Position hinzufügen</span>
            </div>
          </aside>
        </section>

        <footer className="statusbar" aria-live="polite">
          <span>{notice}</span>
          <span>Mandant 100 · Hauptlager MAIN</span>
        </footer>
      </main>
    </div>
  )
}

function euro(value: number) {
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' }).format(value)
}

export default App
