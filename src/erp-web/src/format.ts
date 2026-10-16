const berlinDateTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/Berlin',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
})

export function euro(value: number) {
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' }).format(value)
}

/** Commercial rounding to cents (half away from zero), matching the server's Money.Round. */
export function roundCents(value: number) {
  // toPrecision strips binary noise such as 1.005 * 100 = 100.49999999999999.
  const scaled = Number((Math.abs(value) * 100).toPrecision(15))
  return (Math.sign(value) * Math.round(scaled)) / 100
}

export function qty(value: number) {
  return new Intl.NumberFormat('de-DE', { maximumFractionDigits: 4 }).format(value)
}

/** Formats an ISO/UTC instant as `dd.MM.yyyy / HH:mm:ss` in Europe/Berlin. */
export function formatWhen(value: string) {
  if (!value) return value
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  const parts = berlinDateTime.formatToParts(date)
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? ''

  return `${get('day')}.${get('month')}.${get('year')} / ${get('hour')}:${get('minute')}:${get('second')}`
}

export const taxCodes = [
  { code: 'Standard19', label: '19 %' },
  { code: 'Reduced7', label: '7 %' },
  { code: 'Zero', label: '0 %' },
] as const

export function taxRateLabel(code: string) {
  return taxCodes.find((item) => item.code === code)?.label ?? code
}

export function keepOrFirst(current: string, ids: readonly string[]) {
  return ids.includes(current) ? current : (ids[0] ?? '')
}
