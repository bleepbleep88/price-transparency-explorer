import type { ModifierRef } from '@/features/rates/api/types'
import { modifierText } from '@/features/rates/lib/lenses'
import { ABOUT_THE_SAME, aboutTimes, differenceLine, dollars, feeLabel, positionLabel, TIMES_FROM } from '@/lib/plainNumbers'

// The insights pages' own plain words, built on the shared ones in web/src/lib/plainNumbers.ts (decision 89): the same
// thresholds, so "about the same" means within 5% and "times" starts at twice, everywhere.

/** A figure as dollars() shows it: whole dollars from $100, cents below. */
export function shown(value: number): number {
  return Math.abs(value) >= 100 ? Math.round(value) : Math.round(value * 100) / 100
}

/** A rate as money() shows it, to the cent. */
export function cents(value: number): number {
  return Math.round(value * 100) / 100
}

/**
 * a minus b, taken between the figures as the page shows them so the sum adds up on screen ($1,415 and $1,293 differ
 * by $122, not the $121.40 between the unrounded figures). Rounding can hide a small gap; then the exact one.
 */
export function shownDifference(a: number, b: number, showA = shown, showB = shown): number {
  const difference = showA(a) - showB(b)
  return difference !== 0 ? difference : a - b
}

/**
 * A provider's price against the typical price, as a position and a difference that never contradict each other:
 * - "About typical" only within 5% of typical, and then alone (the shared positionParts rule);
 * - a provider placed "Lower than most" or "Higher than most" always gets its dollars, even within 5%;
 * - from twice the typical price, "about 2.4 times the typical price";
 * - otherwise the dollars and the share they're ranked by: "$221 less than typical (about 87% less)".
 * `unit` (" per unit") goes after the dollars. Without a typical price (under 5 providers), neither.
 */
export function position(band: string | null | undefined, rate: number, typical: number | null | undefined, unit = '') {
  if (typical == null || typical <= 0) return { label: null, difference: null }
  const ratio = rate / typical
  const within = Math.abs(ratio - 1) < ABOUT_THE_SAME
  if (band === 'typical' && within) return { label: positionLabel(band), difference: null }
  const label = band === 'typical' ? null : positionLabel(band)
  if (ratio >= TIMES_FROM) return { label, difference: `${aboutTimes(ratio)} the typical price` }
  if (within && band !== 'low' && band !== 'high') return { label, difference: differenceLine(rate, typical) }
  // The rate shows to the cent, the typical price in whole dollars from $100.
  const gap = shownDifference(rate, typical, cents)
  const direction = gap < 0 ? 'less' : 'more'
  const percent = Math.round(Math.abs(ratio - 1) * 100)
  const share = percent >= ABOUT_THE_SAME * 100 ? ` (about ${percent}% ${direction})` : ''
  return { label, difference: `${dollars(gap)}${unit} ${direction} than typical${share}` }
}

/**
 * A summary point of the ratio one plan pays against the other (the API rounds these, so 1.00 may not be exact): "about
 * the same", "about 20% less", "about 25 times as much". Null without a figure.
 */
export function paysWords(ratio: number | null | undefined): string | null {
  if (ratio == null || Number.isNaN(ratio) || ratio <= 0) return null
  if (ratio >= TIMES_FROM) return `${aboutTimes(ratio)} as much`
  const percent = Math.round(Math.abs(ratio - 1) * 100)
  if (percent === 0) return 'about the same'
  return `about ${percent}% ${ratio < 1 ? 'less' : 'more'}`
}

/** One pair: "the same" only when the two rates are equal; "almost the same ($1.41 more)" within 1%; otherwise as paysWords. */
export function pairWords(pair: { same: boolean; productValue: number; versusValue: number; ratio: number }): string {
  if (pair.same) return 'the same'
  const ratio = pair.productValue / pair.versusValue
  const gap = shownDifference(pair.productValue, pair.versusValue, cents, cents)
  if (Math.abs(ratio - 1) < 0.01) return `almost the same (${dollars(gap)} ${gap < 0 ? 'less' : 'more'})`
  return paysWords(ratio) ?? '—'
}

/**
 * How far most providers' prices run, as the leaderboard's row says it: "most providers get about the same price" within
 * 5%, "the high end is $144 more than the low end" under twice, "the high end is about 6 times the low end" from twice.
 */
export function rangeWords(low: number | null, high: number | null, unit = ''): string | null {
  if (low == null || high == null || low <= 0) return null
  if (high <= low) return 'most providers get the same price'
  const ratio = high / low
  if (ratio - 1 < ABOUT_THE_SAME) return 'most providers get about the same price'
  if (ratio < TIMES_FROM) return `the high end is ${dollars(shownDifference(high, low))}${unit} more than the low end`
  return `the high end is ${aboutTimes(ratio)} the low end`
}

/** "Essential Plan · Facility fee · Ambulatory surgery center · Left side (modifier LT)": the same service, setting and plan. */
export function groupLine(item: { productLabel?: string; feeType: string; settingLabel: string; modifiers: ModifierRef[] }): string {
  const parts = [item.productLabel, feeLabel(item.feeType), item.settingLabel].filter(Boolean)
  const modifiers = item.modifiers.length ? ` · ${modifierText(item.modifiers)}` : ''
  return `${parts.join(' · ')}${modifiers}`
}

/** " per unit" after every dollar figure of a per-unit price (drugs, supplies); nothing otherwise. */
export function unitSuffix(item: { perUnit: boolean }): string {
  return item.perUnit ? ' per unit' : ''
}

/** What a facility charge with no procedure named says instead of a comparison. */
export const FACILITY_CHARGE_MARK = 'Facility charge, no procedure named'

/** Why those charges are left out, said once next to each page's switch. */
export const FACILITY_CHARGE_REASON =
  'Charges filed under a hospital revenue code alone, like "RC 0360 Operating room", cover whatever each hospital bundles under them, so they don\'t compare like for like and are left out unless you include them.'
