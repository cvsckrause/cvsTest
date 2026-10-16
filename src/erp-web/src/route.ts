import { useEffect, useState } from 'react'

export type StammTab = 'articles' | 'addresses'

export type AppRoute =
  | { workspace: 'sales'; documentId: string | null }
  | { workspace: 'stamm'; tab: StammTab; documentId: string | null }
  | { workspace: 'lager' }

export function parseRoute(pathname: string): AppRoute {
  const parts = pathname.split('/').filter(Boolean)
  if (parts[0] === 'lager') return { workspace: 'lager' }
  if (parts[0] === 'stamm') {
    const tab: StammTab = parts[1] === 'addresses' ? 'addresses' : 'articles'
    const documentId = parts[1] === tab ? (parts[2] ?? null) : null
    return { workspace: 'stamm', tab, documentId }
  }
  const documentId = parts[0] === 'sales' ? (parts[1] ?? null) : null
  return { workspace: 'sales', documentId }
}

export function formatRoute(route: AppRoute): string {
  if (route.workspace === 'lager') return '/lager'
  if (route.workspace === 'stamm') {
    return route.documentId ? `/stamm/${route.tab}/${route.documentId}` : `/stamm/${route.tab}`
  }
  return route.documentId ? `/sales/${route.documentId}` : '/sales'
}

const defaultStamm = { workspace: 'stamm', tab: 'articles', documentId: null } as const

export function useRoute() {
  const [route, setRoute] = useState(() => parseRoute(window.location.pathname))
  const [lastStamm, setLastStamm] = useState<Extract<AppRoute, { workspace: 'stamm' }>>(() => {
    const initial = parseRoute(window.location.pathname)
    return initial.workspace === 'stamm' ? initial : { ...defaultStamm }
  })

  function remember(next: AppRoute) {
    setRoute(next)
    if (next.workspace === 'stamm') setLastStamm(next)
  }

  useEffect(() => {
    const canonical = formatRoute(parseRoute(window.location.pathname))
    if (window.location.pathname !== canonical) {
      window.history.replaceState(null, '', canonical)
    }

    const onPopState = () => remember(parseRoute(window.location.pathname))
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  function navigate(next: AppRoute, mode: 'push' | 'replace' = 'push') {
    const path = formatRoute(next)
    if (window.location.pathname !== path) {
      if (mode === 'replace') window.history.replaceState(null, '', path)
      else window.history.pushState(null, '', path)
    }
    remember(parseRoute(path))
  }

  function clearStammDocument() {
    setLastStamm((current) => current.documentId === null ? current : { ...current, documentId: null })
  }

  return { route, navigate, lastStamm, clearStammDocument }
}
