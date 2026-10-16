import { useEffect, useState } from 'react'
import './App.css'
import { erpApi } from './api'
import type { Session } from './api'
import { mandantLabel, useShellAuth } from './shellAuth'
import { getBuildInfo } from './buildInfo'
import { useMasterData } from './master-data'
import { useRoute } from './route'
import type { AppRoute } from './route'
import { useLagerWorkspace } from './workspaces/lager-model'
import { LagerWorkspace } from './workspaces/LagerWorkspace'
import { useSalesWorkspace } from './workspaces/sales-model'
import { SalesWorkspace } from './workspaces/SalesWorkspace'
import { useStammWorkspace } from './workspaces/stamm-model'
import { StammWorkspace } from './workspaces/StammWorkspace'

const buildInfo = getBuildInfo()

function withoutDocument(route: AppRoute): AppRoute {
  if (route.workspace === 'stamm') return { workspace: 'stamm', tab: route.tab, documentId: null }
  if (route.workspace === 'lager') return { workspace: 'lager' }
  return { workspace: 'sales', documentId: null }
}

function TenantStatus({
  route,
  navigate,
  clearStammDocument,
}: {
  route: AppRoute
  navigate: (next: AppRoute, mode?: 'push' | 'replace') => void
  clearStammDocument: () => void
}) {
  const shell = useShellAuth()
  const [session, setSession] = useState<Session | null | undefined>(undefined)

  useEffect(() => {
    let cancelled = false
    erpApi
      .session()
      .then((next) => {
        if (!cancelled) setSession(next)
      })
      .catch(() => {
        if (!cancelled) setSession(null)
      })
    return () => {
      cancelled = true
    }
  }, [])

  function switchTenant(tenantId: string) {
    if (shell === null || tenantId === shell.tenantId) return
    shell.switchTenant(tenantId)
    navigate(withoutDocument(route), 'replace')
    clearStammDocument()
  }

  if (shell === null || shell.memberships.length <= 1) {
    if (session === undefined) return <span>Mandant …</span>
    if (session === null) return <span>Mandant –</span>
    return <span>{mandantLabel(session)}</span>
  }
  return (
    <select
      className="tenant-select"
      aria-label="Mandant wechseln"
      value={shell.tenantId}
      onChange={(event) => switchTenant(event.target.value)}
    >
      {shell.memberships.map((membership) => (
        <option key={membership.tenantId} value={membership.tenantId}>
          {mandantLabel(membership)}
        </option>
      ))}
    </select>
  )
}

function App() {
  const { route, navigate, lastStamm, clearStammDocument } = useRoute()
  const shell = useShellAuth()

  return (
    <Workspace
      key={shell?.tenantId ?? 'local'}
      route={route}
      navigate={navigate}
      lastStamm={lastStamm}
      clearStammDocument={clearStammDocument}
    />
  )
}

function Workspace({
  route,
  navigate,
  lastStamm,
  clearStammDocument,
}: {
  route: AppRoute
  navigate: (next: AppRoute, mode?: 'push' | 'replace') => void
  lastStamm: Extract<AppRoute, { workspace: 'stamm' }>
  clearStammDocument: () => void
}) {
  const [notice, setNotice] = useState('Stammdaten werden geladen …')
  const [busy, setBusy] = useState(false)
  const master = useMasterData(setNotice)
  const salesOrderId = route.workspace === 'sales' ? route.documentId : null
  const sales = useSalesWorkspace({
    orderId: salesOrderId,
    customers: master.customers,
    articles: master.articles,
    notice,
    setNotice,
    busy,
    setBusy,
    navigate,
  })
  const stammTab = route.workspace === 'stamm' ? route.tab : lastStamm.tab
  const stammDocumentId = route.workspace === 'stamm' ? route.documentId : lastStamm.documentId

  function openSales() {
    const next: AppRoute = sales.openRoute
    navigate(next)
    setNotice('Vertrieb')
  }

  function openStamm() {
    navigate(lastStamm)
    setNotice('Stammdaten: Artikel und Adressen verwalten.')
  }

  function openLager() {
    if (route.workspace !== 'lager') {
      setBusy(true)
      setNotice('Lager wird geladen …')
    }
    navigate({ workspace: 'lager' })
  }

  const stamm = useStammWorkspace({
    tab: stammTab,
    documentId: stammDocumentId,
    active: route.workspace === 'stamm',
    customers: master.customers,
    articles: master.articles,
    addresses: master.addresses,
    refreshArticles: master.refreshArticles,
    refreshAddresses: master.refreshAddresses,
    notice,
    setNotice,
    busy,
    setBusy,
    navigate,
    salesCustomerId: sales.selectedCustomerId,
    reloadShippingLinks: sales.reloadShippingLinks,
    openSales,
  })
  const lager = useLagerWorkspace({
    active: route.workspace === 'lager',
    articles: master.articles,
    notice,
    setNotice,
    busy,
    setBusy,
  })

  return (
    <div className="app-shell">
      <aside className="rail">
        <div className="product-mark" aria-label="Northstar ERP">N</div>
        <nav aria-label="Hauptnavigation">
          <button className={`nav-item${route.workspace === 'sales' ? ' active' : ''}`} title="Vertrieb" onClick={openSales}><span>V</span>Vertrieb</button>
          <button className={`nav-item${route.workspace === 'stamm' ? ' active' : ''}`} title="Stammdaten" onClick={openStamm}><span>S</span>Stamm</button>
          <button className={`nav-item${route.workspace === 'lager' ? ' active' : ''}`} title="Lager" onClick={openLager}><span>L</span>Lager</button>
          <button className="nav-item" title="Auswertungen" onClick={() => setNotice('Auswertungen sind in dieser Scheibe nicht enthalten.')}><span>A</span>Analyse</button>
        </nav>
        <div className="rail-footer">CK</div>
      </aside>

      <main className={route.workspace === 'lager' ? 'lager-main' : undefined}>
        {route.workspace === 'sales' && <SalesWorkspace model={sales} />}
        {route.workspace === 'stamm' && <StammWorkspace model={stamm} />}
        {route.workspace === 'lager' && <LagerWorkspace model={lager} />}

        <footer className="statusbar" aria-live="polite">
          <span>{notice}</span>
          <span className="statusbar-meta">
            <TenantStatus route={route} navigate={navigate} clearStammDocument={clearStammDocument} />
            <span className="build-stamp" title="Build identity" aria-label={`Build ${buildInfo.label}`}>
              {buildInfo.label}
            </span>
          </span>
        </footer>
      </main>
    </div>
  )
}

export default App
