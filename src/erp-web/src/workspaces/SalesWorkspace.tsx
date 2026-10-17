import { euro, formatWhen } from '../format'
import type { SalesModel } from './sales-model'

export function SalesWorkspace({ model }: { model: SalesModel }) {
  const {
    notice,
    busy,
    order,
    step,
    orderNumber,
    setOrderNumber,
    warehouseCode,
    setWarehouseCode,
    customers,
    articles,
    shippingLinks,
    billingLinks,
    needsBillToChoice,
    selectedBillToId,
    billToMissing,
    chooseBillTo,
    chooseCustomer,
    sellerIncomplete,
    showFirma,
    lines,
    setLines,
    netAmount,
    selectedCustomerId,
    selectedArticleId,
    selectedShipToId,
    quantity,
    setQuantity,
    setArticleId,
    setShipToAddressId,
    addLine,
    goTo,
    saveOrder,
    releaseOrder,
    cancelOrder,
    startNewOrder,
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
  } = model

  if (showingList) {
    return (
      <>
        <header className="topbar">
          <div>
            <p className="breadcrumb">Vertrieb / Aufträge</p>
            <h1>Auftragsliste</h1>
          </div>
          <div className="system-state"><span /> Entwicklung · Nordics</div>
        </header>

        <section className="workspace sales-list-workspace">
          <div className="document">
            <div className="sales-list-toolbar">
              <label>
                Status
                <select
                  value={listStatus}
                  onChange={(event) => setListStatus(event.target.value as typeof listStatus)}
                >
                  <option value="">Alle</option>
                  <option value="Draft">Entwurf</option>
                  <option value="Released">Freigegeben</option>
                  <option value="Cancelled">Storniert</option>
                </select>
              </label>
              <button className="primary" type="button" onClick={startNewOrder} disabled={busy}>
                Neuer Auftrag
              </button>
            </div>

            <div className="line-grid sales-order-list" role="table" aria-label="Auftragsliste">
              <div className="grid-row grid-head" role="row">
                <span>Nummer</span>
                <span>Status</span>
                <span>Netto</span>
                <span>Erstellt</span>
                <span />
              </div>
              {orders.map((row) => (
                <div className="grid-row" role="row" key={row.id}>
                  <strong>{row.orderNumber}</strong>
                  <span>{statusLabel(row.status)}</span>
                  <span>{euro(row.netAmount)}</span>
                  <span>{formatWhen(row.createdAt)}</span>
                  <button className="secondary" type="button" onClick={() => openOrderFromList(row.id)}>
                    Öffnen
                  </button>
                </div>
              ))}
              {orders.length === 0 && (
                <div className="empty-row">
                  {listBusy ? 'Aufträge werden geladen …' : 'Keine Aufträge für diesen Filter.'}
                </div>
              )}
            </div>
            <p className="action-hint">{notice}</p>
          </div>
        </section>
      </>
    )
  }

  return (
    <>
      <header className="topbar">
        <div>
          <p className="breadcrumb">Vertrieb / Auftragserfassung</p>
          <h1>{order ? order.orderNumber : 'Neuer Auftrag'}</h1>
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
          <div className="document-head document-head-order">
            <label>
              Auftragsnummer
              <input
                value={orderNumber}
                placeholder="automatisch"
                onChange={(event) => setOrderNumber(event.target.value)}
                disabled={Boolean(order)}
              />
            </label>
            <label>
              Lager
              <input value={warehouseCode} onChange={(event) => setWarehouseCode(event.target.value)} disabled={Boolean(order)} />
            </label>
            <label className="customer-field">
              Kunde
              <select
                value={selectedCustomerId}
                onChange={(event) => chooseCustomer(event.target.value)}
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
            {order ? (
              <div className="bill-to-field">
                <span className="field-label">Rechnungsadresse</span>
                <p className="bill-to-readonly">
                  {order.billTo.buyerName} · {order.billTo.address.street}, {order.billTo.address.postalCode} {order.billTo.address.city}
                </p>
              </div>
            ) : needsBillToChoice && (
              <label className="bill-to-field">
                Rechnungsadresse
                <select
                  value={selectedBillToId}
                  onChange={(event) => chooseBillTo(event.target.value)}
                  aria-invalid={billToMissing}
                  aria-describedby={billToMissing ? 'bill-to-missing' : undefined}
                >
                  <option value="">Rechnungsadresse wählen</option>
                  {billingLinks.map((link) => (
                    <option key={link.id} value={link.addressId}>
                      {link.address.name} · {link.address.street}, {link.address.postalCode} {link.address.city}
                    </option>
                  ))}
                </select>
                {billToMissing && <span id="bill-to-missing" className="field-warning">Bitte eine Rechnungsadresse wählen.</span>}
              </label>
            )}
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
                <strong>{euro(line.netAmount)}</strong>
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
            <span className={statusClass(order?.status)}>{statusLabel(order?.status)}</span>
            <span>{orderNumber.trim() || 'automatisch'}</span>
          </div>
          <dl>
            <div><dt>Warenwert</dt><dd>{euro(order?.netAmount ?? netAmount)}</dd></div>
            <div><dt>Rabatt</dt><dd>0,00 €</dd></div>
            <div className="total"><dt>Nettobetrag</dt><dd>{euro(order?.netAmount ?? netAmount)}</dd></div>
          </dl>
          {(step === 'deliver' || step === 'invoice' || order?.status === 'Released') && (
            <dl className="document-refs">
              <div>
                <dt>Lieferung</dt>
                <dd>{deliveries[0]?.number ?? (documentsWaiting ? 'wird erzeugt …' : '—')}</dd>
              </div>
              <div>
                <dt>Rechnung</dt>
                <dd>{invoices[0]?.number ?? (documentsWaiting ? 'wird erzeugt …' : '—')}</dd>
              </div>
            </dl>
          )}
          <div className="summary-actions">
            <p className="action-hint">{notice}</p>
            {sellerIncomplete && (
              <div className="seller-warning" role="alert">
                <p>Ohne vollständige Firmendaten kann keine Rechnung erstellt werden.</p>
                <button className="secondary" type="button" onClick={showFirma}>Firmendaten ergänzen</button>
              </div>
            )}
            {step === 'capture' && !order && <button className="primary" onClick={saveOrder} disabled={busy}>{busy ? 'Speichert …' : 'Auftrag speichern'}</button>}
            {(step === 'release' || (order && order.status !== 'Released')) && order?.status !== 'Released' && order?.status !== 'Cancelled' && (
              <button className="primary" onClick={releaseOrder} disabled={busy || !order}>
                {order ? 'Auftrag freigeben' : 'Zuerst speichern'}
              </button>
            )}
            {order && order.status !== 'Cancelled' && (
              <button className="secondary" type="button" onClick={cancelOrder} disabled={busy}>Stornieren</button>
            )}
            {order?.status === 'Released' && (
              <>
                <p className="released-note">
                  {documentsWaiting
                    ? 'Bestand reserviert. Lieferung und Rechnung werden vom Worker erzeugt — die Nummern erscheinen hier automatisch.'
                    : deliveries[0] && invoices[0]
                      ? `Lieferung ${deliveries[0].number}, Rechnung ${invoices[0].number}.`
                      : 'Bestand reserviert. Belege noch nicht verfügbar.'}
                </p>
                <button className="secondary" type="button" onClick={showOrderList}>Zur Auftragsliste</button>
              </>
            )}
            {order?.status === 'Cancelled' && (
              <>
                <p className="released-note">Der Auftrag ist storniert. Eine Lieferung wird nicht erzeugt.</p>
                <button className="secondary" type="button" onClick={startNewOrder}>Neuen Auftrag erfassen</button>
              </>
            )}
            {!order && (
              <button className="secondary" type="button" onClick={showOrderList}>
                Zur Auftragsliste
              </button>
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

function statusLabel(status?: string) {
  if (status === 'Released') return 'Freigegeben'
  if (status === 'Cancelled') return 'Storniert'
  if (status) return 'Gespeichert'
  return 'Entwurf'
}

function statusClass(status?: string) {
  if (status === 'Released') return 'status released'
  if (status === 'Cancelled') return 'status cancelled'
  return 'status'
}

function stepClass(id: SalesModel['step'], current: SalesModel['step'], order: SalesModel['order']) {
  const released = order?.status === 'Released'
  const done = (id === 'capture' && Boolean(order))
    || (id === 'release' && released)
    || ((id === 'deliver' || id === 'invoice') && released && current !== id)
  if (current === id) return 'workflow-step current'
  if (done) return 'workflow-step done'
  return 'workflow-step'
}
