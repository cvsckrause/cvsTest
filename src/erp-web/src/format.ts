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

export function keepOrFirst(current: string, ids: readonly string[]) {
  return ids.includes(current) ? current : (ids[0] ?? '')
}
