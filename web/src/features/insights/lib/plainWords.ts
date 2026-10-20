import { money, wholeDollars } from '@/lib/format'
import type { ModifierRef } from '@/features/rates/api/types'
import { modifierText } from '@/features/rates/lib/lenses'

// Plain numbers for the insights pages (decision 89): no "median", "percentile", "quartile" or "×". The API keeps its
// precise fields; these turn them into words that claim only what the files support. The shared helper being built in
// web/src/lib/plainNumbers.ts uses the same wording and replaces these once it lands.

/** A dollar amount in a sentence: whole dollars from $100 up, cents below (a $1.50 difference stays $1.50). */
export function dollars(value: number): string {
  const amount = Math.abs(value)
  return amount >= 100 ? wholeDollars(amount) : money(amount)
}

/** "about 2.4 times", "about 20 times": one decimal under 10, whole numbers from 10. */
export function aboutTimes(ratio: number): string {
  const rounded = ratio < 10 ? Math.round(ratio * 10) / 10 : Math.round(ratio)
  return `about ${rounded} times`
}

/**
 * A rate against the typical price (the middle provider's): "$12 less than typical", "about the same as typical"
 * (within 5%), "$29 more than typical", or "about 2.4 times the typical price" from twice up. Null without a typical
 * price (fewer than 5 providers).
 */
export function versusTypical(value: number, typical: number | null): string | null {
  if (typical == null || typical <= 0) return null
  const ratio = value / typical
  if (Math.abs(ratio - 1) < 0.05) return 'about the same as typical'
  if (ratio >= 2) return `${aboutTimes(ratio)} the typical price`
  return `${dollars(value - typical)} ${value < typical ? 'less' : 'more'} than typical`
}

/** Where a provider sits in its group, from the API's band. */
export const POSITION_LABELS: Record<string, string> = {
  low: 'Lower than most',
  typical: 'About typical',
  high: 'Higher than most',
}

/** A provider's overall reading: "Usually about 8% more than typical", "About typical", "Usually about 12% less than typical". */
export function overallText(value: number): string {
  const percent = Math.round(Math.abs(value - 1) * 100)
  if (percent < 5) return 'About typical'
  return `Usually about ${percent}% ${value < 1 ? 'less' : 'more'} than typical`
}

/** What one plan pays against the other for the same provider and service: "the same", "about 20% less", "about 25 times as much". */
export function paysWords(ratio: number): string {
  if (ratio === 1) return 'the same'
  if (ratio >= 2) return `${aboutTimes(ratio)} as much`
  const percent = Math.round(Math.abs(ratio - 1) * 100)
  if (percent === 0) return 'almost the same'
  return `about ${percent}% ${ratio < 1 ? 'less' : 'more'}`
}

/** "Most providers get $A to $B" spread, said plainly: "the high end is about 4 times the low end". */
export function spreadWords(low: number | null, high: number | null): string | null {
  if (low == null || high == null || low <= 0) return null
  if (high <= low) return 'most providers get the same price'
  return `the high end is ${aboutTimes(high / low)} the low end`
}

/** "Doctor's fee" and "Facility fee", the words people use for the two bills. */
export function feeWords(feeType: string): string {
  return feeType === 'professional' ? "Doctor's fee" : feeType === 'facility' ? 'Facility fee' : 'Fee'
}

/** "Essential Plan · Facility fee · Ambulatory surgery center · Left side (modifier LT)": the same service, setting and plan. */
export function groupLine(item: { productLabel?: string; feeType: string; settingLabel: string; modifiers: ModifierRef[] }): string {
  const parts = [item.productLabel, feeWords(item.feeType), item.settingLabel].filter(Boolean)
  const modifiers = item.modifiers.length ? ` · ${modifierText(item.modifiers)}` : ''
  return `${parts.join(' · ')}${modifiers}`
}
