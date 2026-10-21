import { plural } from '@/lib/format'
import { ABOUT_THE_SAME, MIN_PROVIDERS_FOR_TYPICAL, TIMES_FROM, aboutTimes, dollars, feeLabel, fewProvidersLine } from '@/lib/plainNumbers'
import { modifierWords, placesCount } from '@/lib/plainNames'
import { modifierText } from '@/features/rates/lib/lenses'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { AmountSummary, OutOfNetworkFile, OutOfNetworkGroup, OutOfNetworkResult } from '../api/types'

// Plain readings of the out-of-network answer (decision 89). They restate only what the API returned:
// - billed against allowed leads, because it holds whatever an amount covers;
// - the amounts themselves get no "typical" figure: the files don't say whether one covers a visit or several;
// - the 5 rule counts providers, not amounts: under 5 providers the amounts are listed and nothing is compared.
// Dollars, "times", fee names and the 5 and 5% rules come from lib/plainNumbers.ts, like the rest of the app.

export const OUT_OF_NETWORK_NOTES: readonly string[] = [
  'Listed only with 20 or more claims in a 90-day window.',
  'Each different amount is listed once, with no claim count.',
  "The files don't say whether an amount covers one visit or several.",
]

export const PER_UNIT_NOTE = "In-network prices for this code are per unit; these files don't say how many units an amount covers."

/** "20 different amounts listed" (never "payments": the files have no claim counts). */
export function amountsListed(count: number): string {
  return `${plural(count, 'different amount')} listed`
}

/** The groups the search page shows: its product and fee type filters apply here too. */
export function visibleGroups(
  result: OutOfNetworkResult | null | undefined,
  params: Pick<SearchParams, 'product' | 'feeType'>,
): OutOfNetworkGroup[] {
  return (result?.groups ?? []).filter(
    (group) => (!params.product || group.productKey === params.product) && (!params.feeType || group.feeType === params.feeType),
  )
}

/**
 * One line per product whose allowed-amount file was only partly read or couldn't be loaded, under the product filter:
 * otherwise a missing product reads as "no out-of-network amounts".
 */
export function fileNotes(result: OutOfNetworkResult | null | undefined, params: Pick<SearchParams, 'product'>): string[] {
  return (result?.files ?? [])
    .filter((file: OutOfNetworkFile) => !params.product || file.productKey === params.product)
    .flatMap((file) => {
      if (file.status === 'failed') return [`${file.productLabel}'s out-of-network file couldn't be loaded, so its amounts aren't shown.`]
      if (file.status === 'partial') return [`${file.productLabel}'s out-of-network file was only partly read, so some amounts may be missing.`]
      return []
    })
}

/** "Essential Plan · Facility fee", plus the code when one search matched several kinds of code, and any modifiers. */
export function groupLabel(group: OutOfNetworkGroup, withCode = false): string {
  const code = withCode ? ` · ${group.codeType} ${group.code}` : ''
  const modifiers = group.modifiers.length ? ` · ${modifierText(group.modifiers)}` : ''
  return `${group.productLabel} · ${feeLabel(group.feeType)}${code}${modifiers}`
}

/** True when the groups hold more than one kind of code ("450" can be RC 0450 and CPT 00450). */
export function severalCodes(groups: readonly OutOfNetworkGroup[]): boolean {
  return new Set(groups.map((group) => `${group.codeType} ${group.code}`)).size > 1
}

/** "$218, $545, $763 and $8,374" */
export function listText(amounts: readonly number[]): string {
  const shown = amounts.map(dollars)
  return shown.length <= 2 ? shown.join(' and ') : `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}`
}

/**
 * The amounts, never a "typical" one: from 5 providers "ran from $184 to $20,511"; under 5 every amount ("$218 and
 * $545"). The API lists them (median null) exactly when they come from fewer than 5 providers.
 */
export function amountsText(summary: AmountSummary): string {
  if (summary.median == null) return listText(summary.listed)
  return summary.min === summary.max ? `all ${dollars(summary.min)}` : `ran from ${dollars(summary.min)} to ${dollars(summary.max)}`
}

/** "Allowed amounts listed ran from $184 to $20,511." or "Billed charges listed: $218 and $545." */
export function amountsSentence(summary: AmountSummary, noun: 'Allowed amounts' | 'Billed charges'): string {
  return summary.median == null ? `${noun} listed: ${amountsText(summary)}.` : `${noun} listed ${amountsText(summary)}.`
}

/**
 * Billed charges against the allowed amount, from 5 providers: "about 4.8 times", "about 97% more than", "about the
 * same as", "about 20% less than". The caller says against what. Null under 5 providers or without the figure.
 */
export function billedVersusAllowed(group: OutOfNetworkGroup): string | null {
  const ratio = group.billedToAllowed
  if (ratio == null || group.providers < MIN_PROVIDERS_FOR_TYPICAL) return null
  if (Math.abs(ratio - 1) < ABOUT_THE_SAME) return 'about the same as'
  if (ratio >= TIMES_FROM) return aboutTimes(ratio)
  return `about ${Math.round(Math.abs(ratio - 1) * 100)}% ${ratio < 1 ? 'less' : 'more'} than`
}

/** "Providers usually billed about 4.8 times what the plan allowed." Null when there's nothing to compare. */
export function billedLine(group: OutOfNetworkGroup): string | null {
  const versus = billedVersusAllowed(group)
  return versus == null ? null : `Providers usually billed ${versus} what the plan allowed.`
}

/** Why there's no billed-against-allowed figure, for the table cell. */
export function noBilledLine(group: OutOfNetworkGroup): string {
  return group.providers < MIN_PROVIDERS_FOR_TYPICAL ? fewProvidersLine(group.providers) : 'Too few billed charges published to compare.'
}

/** The group a one-line reading speaks for: the most providers, then the most amounts. */
export function leadGroup(groups: readonly OutOfNetworkGroup[]): OutOfNetworkGroup | null {
  return [...groups].sort((a, b) => b.providers - a.providers || b.payments - a.payments)[0] ?? null
}

/** What the plan allowed, when billed charges can't be compared: every amount under 5 providers, else their range. */
function allowedPhrase(group: OutOfNetworkGroup): string {
  return group.allowed.median == null
    ? `the plan allowed ${listText(group.allowed.listed)} (${plural(group.providers, 'provider')})`
    : `the amounts the plan allowed ${amountsText(group.allowed)} (${amountsListed(group.payments)})`
}

/** Plans lens: what providers billed against what the plan allowed out of network. */
export function plansLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  const versus = billedVersusAllowed(lead)
  return versus != null
    ? `Out of network (${where}), providers usually billed ${versus} what the plan allowed, across ${amountsListed(lead.payments)}.`
    : `Out of network (${where}), ${allowedPhrase(lead)}.`
}

/** Providers lens: what providers outside the network billed against what they were allowed. */
export function providersLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  const versus = billedVersusAllowed(lead)
  if (versus != null) {
    return `Providers outside the network usually billed ${versus} what the plan allowed them (${where}, ${amountsListed(lead.payments)}).`
  }
  return lead.allowed.median == null
    ? `Providers outside the network were allowed ${listText(lead.allowed.listed)} (${where}, ${plural(lead.providers, 'provider')}).`
    : `For providers outside the network, the amounts the plan allowed ${amountsText(lead.allowed)} (${where}, ${amountsListed(lead.payments)}).`
}

/** Up to this many amounts are listed one by one on the main view; more read as a range. */
const LISTED_ON_MAIN_VIEW = 4

/** "$184 – $20,511", "$300", or the few listed amounts: "$218 · $545 · $763". */
export function amountsRange(summary: AmountSummary): string {
  if (summary.median == null && summary.listed.length && summary.listed.length <= LISTED_ON_MAIN_VIEW) {
    return summary.listed.map(dollars).join(' · ')
  }
  const [low, high] = [dollars(summary.min), dollars(summary.max)]
  // No-break spaces keep a range on one line.
  return low === high ? low : `${low}\u00a0–\u00a0${high}`
}

/**
 * The panel's one line, for the group with the most providers: "Providers usually billed about 4.8 times what the plan
 * allowed." Without billed charges to compare, what the plan allowed: "The plan allowed $184 – $20,511." Under 5
 * providers, the amounts and how few: "Only 1 place: the plan allowed $218 · $545 · $763 · $8,374." Null without groups.
 */
export function outOfNetworkLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const versus = billedVersusAllowed(lead)
  if (versus != null) return `Providers usually billed ${versus} what the plan allowed.`
  if (lead.providers < MIN_PROVIDERS_FOR_TYPICAL) return `Only ${placesCount(lead.providers)}: the plan allowed ${amountsRange(lead.allowed)}.`
  return `The plan allowed ${amountsRange(lead.allowed)}.`
}

/**
 * Which bill the one line speaks for, when the panel holds more than one: "Facility fee", "Essential Plan · Doctor's fee",
 * "Doctor's fee · left side". No codes. Null with a single group.
 */
export function outOfNetworkWhich(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead || groups.length < 2) return null
  const severalPlans = new Set(groups.map((group) => group.productKey)).size > 1
  return [severalPlans ? lead.productLabel : null, feeLabel(lead.feeType), modifierWords(lead.modifiers)].filter(Boolean).join(' · ')
}
