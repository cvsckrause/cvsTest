import type { SalesOrder } from '../api'
import { euro } from '../format'
import type { SalesModel } from './sales-model'

export function SalesWorkspace({ model }: { model: SalesModel }) {
  const {
    notice,
    busy,
    order,
    step,
    orderNumber,
    setOrderNumber,
    customers,
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
    startNewOrder,
  } = model

  return (
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
          <div className="document-head document-head-three">
            <label>
              Auftragsnummer
              <input value={orderNumber} onChange={(event) => setOrderNumber(event.target.value)} disabled={Boolean(order)} />
            </label>
            <label className="customer-field">
              Kunde
              <select
                value={selectedCustomerId}
                onChange={(event) => {
                  setCustomerId(event.target.value)
                  setShippingLinks([])
                  setShipToAddressId('')
                }}
                disabled={Boolean(order)}
              >
                <option value="">Kunde wählen</option>
                {customers.map((customer) => (
                  <option key={customer.id} value={customer.id}>{customer.number} · {customer.name}</option>
                ))}
              </select>
            </label>
            <label>
              Lieferadresse
              <select value={selectedShipToId} onChange={(event) => setShipToAddressId(event.target.value)} disabled={Boolean(order)}>
                <option value="">Adresse wählen</option>
                {shippingLinks.map((link) => (
                  <option key={link.id} value={link.addressId}>
                    {link.address.name} · {link.address.postalCode} {link.address.city}
                  </option>
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
              <select value={selectedArticleId} onChange={(event) => setArticleId(event.target.value)} disabled={Boolean(order)}>
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
  )
}

function stepClass(id: SalesModel['step'], current: SalesModel['step'], order: SalesOrder | null) {
  const released = order?.status === 'Released'
  const done = (id === 'capture' && Boolean(order))
    || (id === 'release' && released)
    || ((id === 'deliver' || id === 'invoice') && released && current !== id)
  if (current === id) return 'workflow-step current'
  if (done) return 'workflow-step done'
  return 'workflow-step'
}
