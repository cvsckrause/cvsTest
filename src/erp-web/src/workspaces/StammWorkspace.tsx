import { euro } from '../format'
import type { StammModel } from './stamm-model'

export function StammWorkspace({ model }: { model: StammModel }) {
  const {
    tab,
    notice,
    busy,
    customers,
    articles,
    addresses,
    articleForm,
    setArticleForm,
    editingArticleId,
    addressForm,
    setAddressForm,
    editingAddressId,
    selectedLinkCustomerId,
    setLinkCustomerId,
    selectedLinkAddressId,
    setLinkAddressId,
    linkRole,
    setLinkRole,
    showArticles,
    showAddresses,
    editArticle,
    editAddress,
    cancelArticle,
    cancelAddress,
    saveArticle,
    saveAddress,
    linkAddress,
    openSales,
  } = model

  return (
    <>
      <header className="topbar">
        <div>
          <p className="breadcrumb">Stammdaten</p>
          <h1>Artikel & Adressen</h1>
        </div>
        <div className="system-state"><span /> Entwicklung · Nordics</div>
      </header>

      <div className="workflow stamm-tabs" aria-label="Stammdatenbereiche">
        <button type="button" className={tab === 'articles' ? 'workflow-step current' : 'workflow-step'} onClick={showArticles}><span>A</span>Artikel</button>
        <button type="button" className={tab === 'addresses' ? 'workflow-step current' : 'workflow-step'} onClick={showAddresses}><span>D</span>Adressen</button>
      </div>

      <section className="workspace stamm-workspace">
        {tab === 'articles' ? (
          <div className="document">
            <div className="section-title">
              <h2>{editingArticleId ? 'Artikel bearbeiten' : 'Artikel anlegen'}</h2>
              <span>{articles.length} Artikel</span>
            </div>
            <form className="master-form" onSubmit={saveArticle}>
              <label>
                SKU
                <input value={articleForm.sku} onChange={(event) => setArticleForm((current) => ({ ...current, sku: event.target.value }))} disabled={Boolean(editingArticleId)} required={!editingArticleId} />
              </label>
              <label>
                Bezeichnung
                <input value={articleForm.name} onChange={(event) => setArticleForm((current) => ({ ...current, name: event.target.value }))} required />
              </label>
              <label>
                Verkaufspreis
                <input type="number" min="0" step="0.01" value={articleForm.salesPrice} onChange={(event) => setArticleForm((current) => ({ ...current, salesPrice: Number(event.target.value) }))} required />
              </label>
              {!editingArticleId && (
                <label>
                  Anfangsbestand
                  <input type="number" min="0" step="0.01" value={articleForm.openingStock} onChange={(event) => setArticleForm((current) => ({ ...current, openingStock: Number(event.target.value) }))} />
                </label>
              )}
              <div className="form-actions">
                <button className="primary" type="submit" disabled={busy}>{busy ? 'Speichert …' : editingArticleId ? 'Artikel speichern' : 'Artikel anlegen'}</button>
                {editingArticleId && (
                  <button className="secondary" type="button" onClick={cancelArticle}>Abbrechen</button>
                )}
              </div>
            </form>

            <div className="section-title">
              <h2>Artikelliste</h2>
            </div>
            <div className="master-list" role="table" aria-label="Artikelliste">
              <div className="master-row master-head" role="row">
                <span>SKU</span><span>Bezeichnung</span><span>Preis</span><span />
              </div>
              {articles.map((article) => (
                <div className="master-row" role="row" key={article.id}>
                  <strong>{article.sku}</strong>
                  <span>{article.name}</span>
                  <span>{euro(article.salesPrice)}</span>
                  <button className="secondary" type="button" onClick={() => editArticle(article)}>Bearbeiten</button>
                </div>
              ))}
            </div>
          </div>
        ) : (
          <div className="document">
            <div className="section-title">
              <h2>{editingAddressId ? 'Adresse bearbeiten' : 'Adresse anlegen'}</h2>
              <span>{addresses.length} Adressen</span>
            </div>
            <form className="master-form" onSubmit={saveAddress}>
              <label>
                Name
                <input value={addressForm.name} onChange={(event) => setAddressForm((current) => ({ ...current, name: event.target.value }))} required />
              </label>
              <label>
                Straße
                <input value={addressForm.street} onChange={(event) => setAddressForm((current) => ({ ...current, street: event.target.value }))} required />
              </label>
              <label>
                PLZ
                <input value={addressForm.postalCode} onChange={(event) => setAddressForm((current) => ({ ...current, postalCode: event.target.value }))} required />
              </label>
              <label>
                Ort
                <input value={addressForm.city} onChange={(event) => setAddressForm((current) => ({ ...current, city: event.target.value }))} required />
              </label>
              <label>
                Land
                <input value={addressForm.countryCode} onChange={(event) => setAddressForm((current) => ({ ...current, countryCode: event.target.value }))} maxLength={2} required />
              </label>
              <label>
                Zusatz
                <input value={addressForm.additionalLine} onChange={(event) => setAddressForm((current) => ({ ...current, additionalLine: event.target.value }))} />
              </label>
              <div className="form-actions">
                <button className="primary" type="submit" disabled={busy}>{busy ? 'Speichert …' : editingAddressId ? 'Adresse speichern' : 'Adresse anlegen'}</button>
                {editingAddressId && (
                  <button className="secondary" type="button" onClick={cancelAddress}>Abbrechen</button>
                )}
              </div>
            </form>

            <div className="section-title">
              <h2>Kunde verknüpfen</h2>
            </div>
            <form className="master-form link-form" onSubmit={linkAddress}>
              <label>
                Kunde
                <select value={selectedLinkCustomerId} onChange={(event) => setLinkCustomerId(event.target.value)}>
                  {customers.map((customer) => (
                    <option key={customer.id} value={customer.id}>{customer.number} · {customer.name}</option>
                  ))}
                </select>
              </label>
              <label>
                Adresse
                <select value={selectedLinkAddressId} onChange={(event) => setLinkAddressId(event.target.value)}>
                  {addresses.map((address) => (
                    <option key={address.id} value={address.id}>{address.name} · {address.city}</option>
                  ))}
                </select>
              </label>
              <label>
                Rolle
                <select value={linkRole} onChange={(event) => setLinkRole(event.target.value)}>
                  <option value="Shipping">Shipping</option>
                  <option value="Billing">Billing</option>
                </select>
              </label>
              <div className="form-actions">
                <button className="secondary" type="submit" disabled={busy || !selectedLinkCustomerId || !selectedLinkAddressId}>Verknüpfen</button>
              </div>
            </form>

            <div className="section-title">
              <h2>Adressliste</h2>
            </div>
            <div className="master-list" role="table" aria-label="Adressliste">
              <div className="master-row master-head address-row" role="row">
                <span>Name</span><span>Ort</span><span>Land</span><span />
              </div>
              {addresses.map((address) => (
                <div className="master-row address-row" role="row" key={address.id}>
                  <strong>{address.name}</strong>
                  <span>{address.postalCode} {address.city}</span>
                  <span>{address.countryCode}</span>
                  <button className="secondary" type="button" onClick={() => editAddress(address)}>Bearbeiten</button>
                </div>
              ))}
            </div>
          </div>
        )}

        <aside className="summary">
          <div className="summary-heading">
            <span className="status">Stamm</span>
            <span>{tab === 'articles' ? 'Artikel' : 'Adressen'}</span>
          </div>
          <p className="action-hint">{notice}</p>
          <button className="secondary" type="button" onClick={openSales}>Zur Auftragserfassung</button>
        </aside>
      </section>
    </>
  )
}
