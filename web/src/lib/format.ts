// Display formatting. All amounts are US dollars; dates in the data are calendar dates (no time zone).

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const usdWhole = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
const whole = new Intl.NumberFormat('en-US')
const calendar = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' })

export function money(value: number | null | undefined): string {
  return value == null || Number.isNaN(value) ? '—' : usd.format(value)
}

/** Estimates (like a yearly figure) in whole dollars: cents would claim precision they don't have. */
export function wholeDollars(value: number | null | undefined): string {
  return value == null || Number.isNaN(value) ? '—' : usdWhole.format(value)
}

export function wholeNumber(value: number | null | undefined): string {
  return value == null ? '—' : whole.format(value)
}

export function plural(count: number, one: string, many = `${one}s`): string {
  return `${whole.format(count)} ${count === 1 ? one : many}`
}

/** "2026-08-31" or an ISO timestamp → "Aug 31, 2026". */
export function shortDate(value: string | null | undefined): string {
  if (!value) return '—'
  const datePart = value.slice(0, 10)
  const parsed = new Date(`${datePart}T00:00:00Z`)
  return Number.isNaN(parsed.getTime()) ? value : calendar.format(parsed)
}

export function megabytes(bytes: number | null | undefined): string {
  return bytes == null ? '—' : `${whole.format(Math.round(bytes / 1_048_576))} MB`
}
