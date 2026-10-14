import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { erpApi } from './api'
import type { Address, Article, Customer, CustomerAddress, OrderLine, SalesOrder } from './api'
import { createId } from './id'

const initialOrderNumber = `SO-${createId().replaceAll('-', '').slice(0, 8).toUpperCase()}`
type WorkflowStep = 'capture' | 'release' | 'deliver' | 'invoice'
type Workspace = 'sales' | 'stamm'
type StammTab = 'articles' | 'addresses'

function App() {
  const [workspace, setWorkspace] = useState<Workspace>('sales')
  const [stammTab, setStammTab] = useState<StammTab>('articles')
  const [customers, setCustomers] = useState<Customer[]>([])
  const [articles, setArticles] = useState<Article[]>([])
  const [addresses, setAddresses] = useState<Address[]>([])
  const [shippingLinks, setShippingLinks] = useState<CustomerAddress[]>([])
  const [customerId, setCustomerId] = useState('')
  const [shipToAddressId, setShipToAddressId] = useState('')
  const [articleId, setArticleId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [orderNumber, setOrderNumber] = useState(initialOrderNumber)
  const [lines, setLines] = useState<OrderLine[]>([])
  const [order, setOrder] = useState<SalesOrder | null>(null)
  const [step, setStep] = useState<WorkflowStep>('capture')
  const [notice, setNotice] = useState('Stammdaten werden geladen …')
  const [busy, setBusy] = useState(false)

  const [articleForm, setArticleForm] = useState({ sku: '', name: '', salesPrice: 0, openingStock: 0 })
  const [editingArticleId, setEditingArticleId] = useState<string | null>(null)
  const [addressForm, setAddressForm] = useState({
    name: '',
    street: '',
    postalCode: '',
    city: '',
    countryCode: 'DE',
    additionalLine: '',
  })
  const [editingAddressId, setEditingAddressId] = useState<string | null>(null)
  const [linkCustomerId, setLinkCustomerId] = useState('')
  const [linkAddressId, setLinkAddressId] = useState('')
  const [linkRole, setLinkRole] = useState('Shipping')

  useEffect(() => {
    Promise.all([erpApi.customers(), erpApi.articles(), erpApi.addresses()])
      .then(([loadedCustomers, loadedArticles, loadedAddresses]) => {
        setCustomers(loadedCustomers)
        setArticles(loadedArticles)
        setAddresses(loadedAddresses)
        setCustomerId(loadedCustomers[0]?.id ?? '')
        setLinkCustomerId(loadedCustomers[0]?.id ?? '')
        setArticleId(loadedArticles[0]?.id ?? '')
        setLinkAddressId(loadedAddresses[0]?.id ?? '')
        setNotice('Bereit')
      })
      .catch((error: Error) => setNotice(`API nicht erreichbar: ${error.message}`))
  }, [])

  useEffect(() => {
    if (!customerId) {
      setShippingLinks([])
      setShipToAddressId('')
      return
    }

    erpApi.customerAddresses(customerId, 'Shipping')
      .then((links) => {
        setShippingLinks(links)
        setShipToAddressId((current) =>
          links.some((link) => link.addressId === current) ? current : (links[0]?.addressId ?? ''))
      })
      .catch((error: Error) => setNotice(`Lieferadressen nicht ladbar: ${error.message}`))
  }, [customerId])

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
    if (!customerId || !shipToAddressId || lines.length === 0) {
      setNotice('Kunde, Lieferadresse und mindestens eine Position sind erforderlich.')
      return
    }
    setBusy(true)
    try {
      const saved = await erpApi.createOrder(customerId, shipToAddressId, orderNumber, lines)
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

  async function refreshMasterData() {
    const [loadedArticles, loadedAddresses] = await Promise.all([erpApi.articles(), erpApi.addresses()])
    setArticles(loadedArticles)
    setAddresses(loadedAddresses)
    setArticleId((current) => loadedArticles.some((item) => item.id === current) ? current : (loadedArticles[0]?.id ?? ''))
    setLinkAddressId((current) => loadedAddresses.some((item) => item.id === current) ? current : (loadedAddresses[0]?.id ?? ''))
  }

  async function saveArticle(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      if (editingArticleId) {
        await erpApi.updateArticle(editingArticleId, articleForm.name, articleForm.salesPrice)
        setNotice(`Artikel ${articleForm.sku || editingArticleId} aktualisiert.`)
      } else {
        await erpApi.createArticle(articleForm.sku, articleForm.name, articleForm.salesPrice, articleForm.openingStock)
        setNotice(`Artikel ${articleForm.sku} angelegt.`)
      }
      setEditingArticleId(null)
      setArticleForm({ sku: '', name: '', salesPrice: 0, openingStock: 0 })
      await refreshMasterData()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Artikel konnte nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  async function saveAddress(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      const payload = {
        name: addressForm.name,
        street: addressForm.street,
        postalCode: addressForm.postalCode,
        city: addressForm.city,
        countryCode: addressForm.countryCode,
        additionalLine: addressForm.additionalLine || null,
      }
      if (editingAddressId) {
        await erpApi.updateAddress(editingAddressId, payload)
        setNotice('Adresse aktualisiert.')
      } else {
        await erpApi.createAddress(payload)
        setNotice('Adresse angelegt.')
      }
      setEditingAddressId(null)
      setAddressForm({ name: '', street: '', postalCode: '', city: '', countryCode: 'DE', additionalLine: '' })
      await refreshMasterData()
      if (customerId) {
        const links = await erpApi.customerAddresses(customerId, 'Shipping')
        setShippingLinks(links)
      }
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Adresse konnte nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  async function linkAddress(event: FormEvent) {
    event.preventDefault()
    if (!linkCustomerId || !linkAddressId) return
    setBusy(true)
    try {
      await erpApi.linkCustomerAddress(linkCustomerId, linkAddressId, linkRole)
      setNotice(`Adresse als ${linkRole} verknüpft.`)
      if (linkCustomerId === customerId) {
        const links = await erpApi.customerAddresses(customerId, 'Shipping')
        setShippingLinks(links)
        if (!shipToAddressId && links[0]) setShipToAddressId(links[0].addressId)
      }
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Verknüpfung fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  function editArticle(article: Article) {
    setEditingArticleId(article.id)
    setArticleForm({ sku: article.sku, name: article.name, salesPrice: article.salesPrice, openingStock: 0 })
    setStammTab('articles')
  }

  function editAddress(address: Address) {
    setEditingAddressId(address.id)
    setAddressForm({
      name: address.name,
      street: address.street,
      postalCode: address.postalCode,
      city: address.city,
      countryCode: address.countryCode,
      additionalLine: address.additionalLine ?? '',
    })
    setStammTab('addresses')
  }

  return (
    <div className="app-shell">
      <aside className="rail">
        <div className="product-mark" aria-label="Northstar ERP">N</div>
        <nav aria-label="Hauptnavigation">
          <button className={`nav-item${workspace === 'sales' ? ' active' : ''}`} title="Vertrieb" onClick={() => setWorkspace('sales')}><span>V</span>Vertrieb</button>
          <button className={`nav-item${workspace === 'stamm' ? ' active' : ''}`} title="Stammdaten" onClick={() => { setWorkspace('stamm'); setNotice('Stammdaten: Artikel und Adressen verwalten.') }}><span>S</span>Stamm</button>
          <button className="nav-item" title="Lager" onClick={() => setNotice('Lagerbuchungen laufen über die Freigabe. Eine eigene Lager-Maske gibt es in dieser Scheibe nicht.')}><span>L</span>Lager</button>
          <button className="nav-item" title="Auswertungen" onClick={() => setNotice('Auswertungen sind in dieser Scheibe nicht enthalten.')}><span>A</span>Analyse</button>
        </nav>
        <div className="rail-footer">CK</div>
      </aside>

      <main>
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
                <div className="document-head document-head-three">
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
                  <label>
                    Lieferadresse
                    <select value={shipToAddressId} onChange={(event) => setShipToAddressId(event.target.value)} disabled={Boolean(order)}>
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
                <p className="breadcrumb">Stammdaten</p>
                <h1>Artikel & Adressen</h1>
              </div>
              <div className="system-state"><span /> Entwicklung · Nordics</div>
            </header>

            <div className="workflow stamm-tabs" aria-label="Stammdatenbereiche">
              <button type="button" className={stammTab === 'articles' ? 'workflow-step current' : 'workflow-step'} onClick={() => setStammTab('articles')}><span>A</span>Artikel</button>
              <button type="button" className={stammTab === 'addresses' ? 'workflow-step current' : 'workflow-step'} onClick={() => setStammTab('addresses')}><span>D</span>Adressen</button>
            </div>

            <section className="workspace stamm-workspace">
              {stammTab === 'articles' ? (
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
                        <button className="secondary" type="button" onClick={() => { setEditingArticleId(null); setArticleForm({ sku: '', name: '', salesPrice: 0, openingStock: 0 }) }}>Abbrechen</button>
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
                        <button className="secondary" type="button" onClick={() => { setEditingAddressId(null); setAddressForm({ name: '', street: '', postalCode: '', city: '', countryCode: 'DE', additionalLine: '' }) }}>Abbrechen</button>
                      )}
                    </div>
                  </form>

                  <div className="section-title">
                    <h2>Kunde verknüpfen</h2>
                  </div>
                  <form className="master-form link-form" onSubmit={linkAddress}>
                    <label>
                      Kunde
                      <select value={linkCustomerId} onChange={(event) => setLinkCustomerId(event.target.value)}>
                        {customers.map((customer) => (
                          <option key={customer.id} value={customer.id}>{customer.number} · {customer.name}</option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Adresse
                      <select value={linkAddressId} onChange={(event) => setLinkAddressId(event.target.value)}>
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
                      <button className="secondary" type="submit" disabled={busy || !linkCustomerId || !linkAddressId}>Verknüpfen</button>
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
                  <span>{stammTab === 'articles' ? 'Artikel' : 'Adressen'}</span>
                </div>
                <p className="action-hint">{notice}</p>
                <button className="secondary" type="button" onClick={() => setWorkspace('sales')}>Zur Auftragserfassung</button>
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

export default App
