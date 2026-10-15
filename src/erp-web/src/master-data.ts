import { useEffect, useState } from 'react'
import { erpApi } from './api'
import type { Address, Article, Customer } from './api'

export function useMasterData(setNotice: (notice: string) => void) {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [articles, setArticles] = useState<Article[]>([])
  const [addresses, setAddresses] = useState<Address[]>([])

  useEffect(() => {
    let cancelled = false
    Promise.all([erpApi.customers(), erpApi.articles()])
      .then(async ([loadedCustomers, loadedArticles]) => {
        if (cancelled) return
        setCustomers(loadedCustomers)
        setArticles(loadedArticles)
        try {
          const loadedAddresses = await erpApi.addresses()
          if (cancelled) return
          setAddresses(loadedAddresses)
          setNotice('Bereit')
        } catch (error) {
          if (cancelled) return
          setNotice(
            error instanceof Error
              ? `Artikel geladen. Adressen-API fehlt noch (${error.message})`
              : 'Artikel geladen. Adressen-API fehlt noch.',
          )
        }
      })
      .catch((error: Error) => {
        if (!cancelled) setNotice(`API nicht erreichbar: ${error.message}`)
      })

    return () => {
      cancelled = true
    }
  }, [setNotice])

  async function refreshArticles() {
    const loadedArticles = await erpApi.articles()
    setArticles(loadedArticles)
    return loadedArticles
  }

  async function refreshAddresses() {
    const loadedAddresses = await erpApi.addresses()
    setAddresses(loadedAddresses)
    return loadedAddresses
  }

  return { customers, articles, addresses, refreshArticles, refreshAddresses }
}
