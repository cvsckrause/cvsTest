import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, erpApi } from '../api'
import type { Address, Article, Customer, SellerDetails } from '../api'
import { keepOrFirst } from '../format'
import type { AppRoute, StammTab } from '../route'

const emptyArticle = { sku: '', name: '', salesPrice: 0, openingStock: 0, taxCode: 'Standard19' }
const emptyAddress = { name: '', street: '', postalCode: '', city: '', countryCode: 'DE', additionalLine: '' }
const emptySeller = { street: '', postalCode: '', city: '', countryCode: 'DE', additionalLine: '', steuernummer: '', ustIdNr: '' }

type StammModelArgs = {
  tab: StammTab
  documentId: string | null
  active: boolean
  customers: Customer[]
  articles: Article[]
  addresses: Address[]
  refreshArticles: () => Promise<Article[]>
  refreshAddresses: () => Promise<Address[]>
  notice: string
  setNotice: (notice: string) => void
  busy: boolean
  setBusy: (busy: boolean) => void
  navigate: (route: AppRoute, mode?: 'push' | 'replace') => void
  salesCustomerId: string
  reloadShippingLinks: (customerId: string, selectFirstIfEmpty: boolean) => Promise<void>
  openSales: () => void
}

export function useStammWorkspace({
  tab,
  documentId,
  active,
  customers,
  articles,
  addresses,
  refreshArticles,
  refreshAddresses,
  notice,
  setNotice,
  busy,
  setBusy,
  navigate,
  salesCustomerId,
  reloadShippingLinks,
  openSales,
}: StammModelArgs) {
  const [articleForm, setArticleForm] = useState(emptyArticle)
  const [editingArticleId, setEditingArticleId] = useState<string | null>(null)
  const [addressForm, setAddressForm] = useState(emptyAddress)
  const [editingAddressId, setEditingAddressId] = useState<string | null>(null)
  const [linkCustomerId, setLinkCustomerId] = useState('')
  const [linkAddressId, setLinkAddressId] = useState('')
  const [linkRole, setLinkRole] = useState('Shipping')
  const [seller, setSeller] = useState<SellerDetails | null>(null)
  const [sellerForm, setSellerForm] = useState(emptySeller)
  const [sellerProblem, setSellerProblem] = useState<string | null>(null)

  const selectedLinkCustomerId = keepOrFirst(linkCustomerId, customers.map((customer) => customer.id))
  const selectedLinkAddressId = keepOrFirst(linkAddressId, addresses.map((address) => address.id))

  useEffect(() => {
    if (!active || tab !== 'articles' || !documentId || editingArticleId === documentId) return
    const known = articles.find((item) => item.id === documentId)
    let cancelled = false
    const apply = (article: Article) => {
      if (cancelled) return
      setEditingArticleId(article.id)
      setArticleForm(articleFormOf(article))
    }
    if (known) apply(known)
    else {
      erpApi.article(documentId).then(apply).catch((error: Error) => {
        if (!cancelled) setNotice(error.message)
      })
    }
    return () => {
      cancelled = true
    }
  }, [active, tab, documentId, articles, editingArticleId, setNotice])

  useEffect(() => {
    if (!active || tab !== 'addresses' || !documentId || editingAddressId === documentId) return
    const known = addresses.find((item) => item.id === documentId)
    let cancelled = false
    const apply = (address: Address) => {
      if (cancelled) return
      setEditingAddressId(address.id)
      setAddressForm({
        name: address.name,
        street: address.street,
        postalCode: address.postalCode,
        city: address.city,
        countryCode: address.countryCode,
        additionalLine: address.additionalLine ?? '',
      })
    }
    if (known) apply(known)
    else {
      erpApi.address(documentId).then(apply).catch((error: Error) => {
        if (!cancelled) setNotice(error.message)
      })
    }
    return () => {
      cancelled = true
    }
  }, [active, tab, documentId, addresses, editingAddressId, setNotice])

  useEffect(() => {
    if (!active || tab !== 'firma') return
    let cancelled = false
    erpApi.sellerDetails()
      .then((loaded) => {
        if (cancelled) return
        setSellerProblem(null)
        applySeller(loaded)
      })
      .catch((error: Error) => {
        if (cancelled) return
        setSellerProblem(
          error instanceof ApiError && error.status === 404
            ? 'Für diesen Mandanten sind keine Firmendaten hinterlegt.'
            : 'Firmendaten konnten nicht geladen werden. Tab erneut öffnen, um es noch einmal zu versuchen.',
        )
        setNotice(error.message)
      })
    return () => {
      cancelled = true
    }
  }, [active, tab, setNotice])

  function applySeller(loaded: SellerDetails) {
    setSeller(loaded)
    setSellerForm(sellerFormOf(loaded))
  }

  function showFirma() {
    navigate({ workspace: 'stamm', tab: 'firma', documentId: null })
  }

  async function saveSeller(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      const saved = await erpApi.updateSellerDetails({
        street: sellerForm.street,
        postalCode: sellerForm.postalCode,
        city: sellerForm.city,
        countryCode: sellerForm.countryCode,
        additionalLine: sellerForm.additionalLine || null,
        steuernummer: sellerForm.steuernummer || null,
        ustIdNr: sellerForm.ustIdNr || null,
      })
      applySeller(saved)
      setNotice('Firmendaten gespeichert.')
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Firmendaten konnten nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  function showArticles() {
    navigate({ workspace: 'stamm', tab: 'articles', documentId: editingArticleId })
  }

  function showAddresses() {
    navigate({ workspace: 'stamm', tab: 'addresses', documentId: editingAddressId })
  }

  function editArticle(article: Article) {
    setEditingArticleId(article.id)
    setArticleForm(articleFormOf(article))
    navigate({ workspace: 'stamm', tab: 'articles', documentId: article.id })
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
    navigate({ workspace: 'stamm', tab: 'addresses', documentId: address.id })
  }

  function cancelArticle() {
    setEditingArticleId(null)
    setArticleForm(emptyArticle)
    navigate({ workspace: 'stamm', tab: 'articles', documentId: null })
  }

  function cancelAddress() {
    setEditingAddressId(null)
    setAddressForm(emptyAddress)
    navigate({ workspace: 'stamm', tab: 'addresses', documentId: null })
  }

  async function saveArticle(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      if (editingArticleId) {
        await erpApi.updateArticle(editingArticleId, articleForm.name, articleForm.salesPrice, articleForm.taxCode)
        setNotice(`Artikel ${articleForm.sku || editingArticleId} aktualisiert.`)
      } else {
        await erpApi.createArticle(articleForm.sku, articleForm.name, articleForm.salesPrice, articleForm.openingStock, articleForm.taxCode)
        setNotice(`Artikel ${articleForm.sku} angelegt.`)
      }
      setEditingArticleId(null)
      setArticleForm(emptyArticle)
      navigate({ workspace: 'stamm', tab: 'articles', documentId: null }, 'replace')
      await refreshArticles()
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
      setAddressForm(emptyAddress)
      navigate({ workspace: 'stamm', tab: 'addresses', documentId: null }, 'replace')
      await refreshAddresses()
      await reloadShippingLinks(salesCustomerId, false)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Adresse konnte nicht gespeichert werden.')
    } finally {
      setBusy(false)
    }
  }

  async function linkAddress(event: FormEvent) {
    event.preventDefault()
    if (!selectedLinkCustomerId || !selectedLinkAddressId) return
    setBusy(true)
    try {
      await erpApi.linkCustomerAddress(selectedLinkCustomerId, selectedLinkAddressId, linkRole)
      setNotice(`Adresse als ${linkRole} verknüpft.`)
      await reloadShippingLinks(selectedLinkCustomerId, true)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Verknüpfung fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  return {
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
    showFirma,
    seller,
    sellerForm,
    setSellerForm,
    sellerProblem,
    saveSeller,
    editArticle,
    editAddress,
    cancelArticle,
    cancelAddress,
    saveArticle,
    saveAddress,
    linkAddress,
    openSales,
  }
}

function articleFormOf(article: Article) {
  return { sku: article.sku, name: article.name, salesPrice: article.salesPrice, openingStock: 0, taxCode: article.taxCode }
}

function sellerFormOf(seller: SellerDetails) {
  return {
    street: seller.street ?? '',
    postalCode: seller.postalCode ?? '',
    city: seller.city ?? '',
    countryCode: seller.countryCode ?? emptySeller.countryCode,
    additionalLine: seller.additionalLine ?? '',
    steuernummer: seller.steuernummer ?? '',
    ustIdNr: seller.ustIdNr ?? '',
  }
}

export type StammModel = ReturnType<typeof useStammWorkspace>

