export function euro(value: number) {
  return new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' }).format(value)
}

export function qty(value: number) {
  return new Intl.NumberFormat('de-DE', { maximumFractionDigits: 4 }).format(value)
}

export function formatWhen(value: string) {
  return new Intl.DateTimeFormat('de-DE', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function keepOrFirst(current: string, ids: readonly string[]) {
  return ids.includes(current) ? current : (ids[0] ?? '')
}
