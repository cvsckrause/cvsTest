import { formatWhen, qty } from '../format'
import type { LagerModel } from './lager-model'

function movementLabel(movementType: string) {
  if (movementType === 'GoodsReceipt') return 'Wareneingang'
  if (movementType === 'GoodsIssue') return 'Warenausgang'
  return movementType
}

export function LagerWorkspace({ model }: { model: LagerModel }) {
  const {
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
  } = model

  return (
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
                <span>{movementLabel(movement.movementType)}</span>
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
              <select value={selectedReceiptArticleId} onChange={(event) => setReceiptArticleId(event.target.value)}>
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
  )
}
