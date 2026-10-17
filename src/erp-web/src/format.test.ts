import { expect, test } from 'vitest'
import { formatWhen, roundCents } from './format'

test('rounds cents half away from zero like the server', () => {
  expect(roundCents(0.125)).toBe(0.13)
  expect(roundCents(1.005)).toBe(1.01)
  expect(roundCents(2.675)).toBe(2.68)
  expect(roundCents(-0.125)).toBe(-0.13)
  expect(roundCents(3 * 0.3335)).toBe(1)
  expect(roundCents(0.0049)).toBe(0)
})

test('formats a summer UTC instant in Europe/Berlin as dd.MM.yyyy / HH:mm:ss', () => {
  expect(formatWhen('2026-10-15T10:00:00.000Z')).toBe('15.10.2026 / 12:00:00')
})

test('formats a winter UTC instant in Europe/Berlin (CET)', () => {
  expect(formatWhen('2026-01-15T10:00:00.000Z')).toBe('15.01.2026 / 11:00:00')
})

test('returns the raw string when the value is not a valid date', () => {
  expect(formatWhen('not-a-date')).toBe('not-a-date')
  expect(formatWhen('')).toBe('')
})
