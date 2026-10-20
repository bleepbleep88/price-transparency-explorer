// Provider identifier rules, mirroring the API (Core/Domain/ProviderIds.cs).

export function npiDigits(input: string | null | undefined): string {
  return (input ?? '').replace(/\D/g, '')
}

/**
 * CMS NPI check digit: 10 digits starting with 1 or 2, Luhn computed as if the prefix 80840 were present
 * (the constant 24 stands in for that prefix).
 */
export function isValidNpi(input: string | null | undefined): boolean {
  const digits = npiDigits(input)
  if (!/^[12]\d{9}$/.test(digits)) return false

  let sum = 24
  let double = true
  for (let i = 8; i >= 0; i--) {
    let digit = Number(digits[i])
    if (double) {
      digit *= 2
      if (digit > 9) digit -= 9
    }
    sum += digit
    double = !double
  }
  return (10 - (sum % 10)) % 10 === Number(digits[9])
}

/** "11-3471596", "113471596" or "11 3471596" → "113471596"; anything else → null. */
/** The API sends NPIs as numbers, which drops a leading zero (124507857); NPIs are always 10 digits. */
export function formatNpi(npi: number | string): string {
  return String(npi).padStart(10, '0')
}

export function normalizeEin(input: string | null | undefined): string | null {
  const compact = (input ?? '').replace(/[\s-]/g, '')
  return /^\d{9}$/.test(compact) ? compact : null
}

export function formatEin(nineDigits: string): string {
  return `${nineDigits.slice(0, 2)}-${nineDigits.slice(2)}`
}

/** Provider keys stand in for masked tax IDs in links: "u" + 12 hex characters. */
export function isProviderKey(input: string): boolean {
  return /^[uU][0-9a-fA-F]{12}$/.test(input.trim())
}

export function isEinOrProviderKey(input: string): boolean {
  const value = input.trim()
  return isProviderKey(value) || (/^[\d\s-]+$/.test(value) && normalizeEin(value) !== null)
}
