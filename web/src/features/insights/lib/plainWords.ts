import type { ModifierRef } from '@/features/rates/api/types'
import { modifierText } from '@/features/rates/lib/lenses'
import { billName, serviceTitle } from '@/lib/plainNames'
import { ABOUT_THE_SAME, aboutTimes, capitalize, differenceDollars, feeLabel, minus, shownDollars, TIMES_FROM } from '@/lib/plainNumbers'

// The insights pages' own plain words, built on the shared ones in web/src/lib/plainNumbers.ts: the same thresholds, so
// "about" means within 5% and "times" starts at twice, everywhere (decision 89), in as few words as can say it (90).

/** A rate as money() shows it, to the cent. */
export function cents(value: number): number {
  return Math.round(value * 100) / 100
}

/**
 * A price against the typical price in three or four words: "18% above typical", "About 2.1 times typical", "About
 * typical". A price placed lower or higher than most (`band`) gets its dollars even within 5% ("$0.95 below typical"),
 * taken between the rate as shown (to the cent) and the typical price as shown, so the two never contradict each other.
 * Null without a typical price (under 5 places).
 */
export function versusTypical(rate: number, typical: number | null | undefined, band?: string | null): string | null {
  if (typical == null || typical <= 0) return null
  const ratio = rate / typical
  if (ratio >= TIMES_FROM) return `${capitalize(aboutTimes(ratio))} typical`
  if (Math.abs(ratio - 1) >= ABOUT_THE_SAME) return `${Math.round(Math.abs(ratio - 1) * 100)}% ${ratio < 1 ? 'below' : 'above'} typical`
  if (band !== 'low' && band !== 'high') return 'About typical'
  const gap = minus(cents(rate), shownDollars(typical))
  // Rounding the typical price can leave no difference, or one the wrong way: then only "about".
  if (gap === 0 || (band === 'low' && gap > 0) || (band === 'high' && gap < 0)) return 'About typical'
  return `${differenceDollars(gap)} ${gap < 0 ? 'below' : 'above'} typical`
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
  if (Math.abs(ratio - 1) < 0.01) {
    // Both rates show to the cent; the difference is between those.
    const gap = minus(cents(pair.productValue), cents(pair.versusValue))
    return gap === 0 ? 'almost the same' : `almost the same (${differenceDollars(gap)} ${gap < 0 ? 'less' : 'more'})`
  }
  return paysWords(ratio) ?? '—'
}

/** The dollars between the two ends of a range as they're shown ("$389 – $625" → "$236"). */
export function spreadDollars(low: number, high: number): string {
  return differenceDollars(minus(shownDollars(high), shownDollars(low)))
}

/** "$389 – $625", or one price when both ends are the same. */
export function rangeText(low: number, high: number, format: (value: number) => string): string {
  return format(low) === format(high) ? format(low) : `${format(low)} – ${format(high)}`
}

/**
 * What to call a service on the main view: its plain name ("Colonoscopy", "Office visit, level 3"), or "Facility charge"
 * for a hospital revenue code nobody named, instead of "Code 0460".
 */
export function serviceLabel(item: { codeType: string; code: string; codeName: string | null }): string {
  if (!item.codeName && item.codeType === 'RC') return 'Facility charge'
  return serviceTitle(item.code, item.codeName)
}

/** A plan's everyday name: "Ambetter from Fidelis Care" → "Ambetter"; "Essential Plan" stays. */
export function planName(label: string): string {
  return label.replace(/\s+from\s+.+$/i, '').trim() || label
}

/**
 * Modifiers in at most two short phrases, without the bracketed parts or codes (one with no plain name is left out):
 * "left side, right side", "separate
 * visit on the same day as a procedure, +2 more". A video visit drops its own telehealth modifiers (the bill says video).
 */
export function fewModifiers(modifiers: ModifierRef[], settingKey = ''): string {
  const phrases = modifiers
    .map((modifier) => modifier.label.replace(/\s*\([^)]*\)/g, '').trim())
    // A modifier with no plain name reads "Modifier SS": that's a code, so it stays in Details.
    .filter((label) => label && !/^modifier\b/i.test(label) && !(settingKey.startsWith('telehealth') && /^telehealth/i.test(label)))
    .map((label) => label.charAt(0).toLowerCase() + label.slice(1))
  const unique = [...new Set(phrases)]
  if (unique.length <= 2) return unique.join(', ')
  return `${unique[0]}, +${unique.length - 1} more`
}

/**
 * The second line under a service on the main view: the bill in plain words, its modifiers in short, the plan, and
 * "per unit" for drugs and supplies. "Doctor · in the office · Essential Plan", "Surgery center · Ambetter".
 */
export function rowLine(item: {
  feeType: string
  settingKey: string
  settingLabel: string
  modifiers: ModifierRef[]
  productLabel?: string
  perUnit: boolean
}): string {
  const { name, where } = billName(item)
  return [name, where, fewModifiers(item.modifiers, item.settingKey), item.productLabel ? planName(item.productLabel) : null, item.perUnit ? 'per unit' : null]
    .filter(Boolean)
    .join(' · ')
}

/** "Essential Plan · Facility fee · Ambulatory surgery center · Left side (modifier LT)": the precise group, for Details and the filter. */
export function groupLine(item: { productLabel?: string; feeType: string; settingLabel: string; modifiers: ModifierRef[] }): string {
  const parts = [item.productLabel, feeLabel(item.feeType), item.settingLabel].filter(Boolean)
  const modifiers = item.modifiers.length ? ` · ${modifierText(item.modifiers)}` : ''
  return `${parts.join(' · ')}${modifiers}`
}

/** " per unit" after a dollar figure of a per-unit price (drugs, supplies); nothing otherwise. */
export function unitSuffix(item: { perUnit: boolean }): string {
  return item.perUnit ? ' per unit' : ''
}

/** What a scorecard row for a facility charge with no procedure named says instead of a comparison. */
export const FACILITY_CHARGE_MARK = 'Facility-only charge'

/** Why those charges are left out, for Details. */
export const FACILITY_CHARGE_REASON =
  'Facility-only charges are filed under a hospital revenue code with no procedure named, like "RC 0360 Operating room". ' +
  "Each hospital bundles different things under them, so they don't compare like for like and are left out unless you include them."
