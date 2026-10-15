import { expect, test } from 'vitest'
import { formatBuildStamp, getBuildInfo } from './buildInfo'

test('formats a production build stamp', () => {
  expect(formatBuildStamp('0.1.0', '2026-10-15T13:42:00.000Z', 'a1b2c3d')).toBe(
    'v0.1.0 · 2026-10-15 13:42 UTC · a1b2c3d',
  )
})

test('falls back for local Vite when env vars are missing', () => {
  expect(getBuildInfo({})).toEqual({
    version: 'dev',
    buildTime: 'local',
    gitSha: 'unknown',
    label: 'vdev · local · unknown',
  })
})

test('uses injected env values when present', () => {
  expect(getBuildInfo({
    VITE_APP_VERSION: '0.1.0',
    VITE_APP_BUILD_TIME: '2026-10-15T13:42:00Z',
    VITE_APP_GIT_SHA: 'be27a82',
  }).label).toBe('v0.1.0 · 2026-10-15 13:42 UTC · be27a82')
})
