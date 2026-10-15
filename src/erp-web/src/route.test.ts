import { expect, test } from 'vitest'
import { formatRoute, parseRoute } from './route'

test('names the sales workspace and an open order', () => {
  expect(parseRoute('/')).toEqual({ workspace: 'sales', documentId: null })
  expect(parseRoute('/sales')).toEqual({ workspace: 'sales', documentId: null })
  expect(parseRoute('/sales/new')).toEqual({ workspace: 'sales', documentId: 'new' })
  expect(parseRoute('/sales/order-1')).toEqual({ workspace: 'sales', documentId: 'order-1' })
  expect(formatRoute({ workspace: 'sales', documentId: 'new' })).toBe('/sales/new')
  expect(formatRoute({ workspace: 'sales', documentId: 'order-1' })).toBe('/sales/order-1')
})

test('names the stamm tab and the open master-data document', () => {
  expect(parseRoute('/stamm')).toEqual({ workspace: 'stamm', tab: 'articles', documentId: null })
  expect(parseRoute('/stamm/addresses')).toEqual({ workspace: 'stamm', tab: 'addresses', documentId: null })
  expect(parseRoute('/stamm/articles/article-1')).toEqual({
    workspace: 'stamm',
    tab: 'articles',
    documentId: 'article-1',
  })
  expect(formatRoute({ workspace: 'stamm', tab: 'addresses', documentId: 'address-1' })).toBe(
    '/stamm/addresses/address-1',
  )
})

test('names the lager workspace', () => {
  expect(parseRoute('/lager')).toEqual({ workspace: 'lager' })
  expect(formatRoute({ workspace: 'lager' })).toBe('/lager')
})
