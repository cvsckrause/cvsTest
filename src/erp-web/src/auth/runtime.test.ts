import { afterEach, expect, test } from 'vitest'
import { createLocalAdapter } from './localAdapter'
import { bootstrapAuth, resetAuthRuntime } from './runtime'

afterEach(() => {
  resetAuthRuntime()
})

test('bootstrapAuth reuses the completed adapter on a second call', async () => {
  let selections = 0
  const adapter = createLocalAdapter()
  const select = () => {
    selections += 1
    return adapter
  }

  const first = await bootstrapAuth(async () => ({ mode: 'local' }), select)
  const second = await bootstrapAuth(async () => ({ mode: 'local' }), select)

  expect(selections).toBe(1)
  expect(second.adapter).toBe(first.adapter)
  expect(second.session.authenticated).toBe(true)
})
