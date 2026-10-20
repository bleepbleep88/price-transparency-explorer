import { money, plural, wholeDollars } from '@/lib/format'
import { modifierText } from '@/features/rates/lib/lenses'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { AmountSummary, OutOfNetworkGroup, OutOfNetworkResult } from '../api/types'

// Plain readings of the out-of-network answer (decision 89: no "median" or other statistics words). They restate only
// what the API returned. dollars, aboutTimes and feeLabel follow the same rules as lib/plainNumbers.ts on
// feat/plain-numbers, so the wording matches the rest of the app.

export const OUT_OF_NETWORK_NOTES: readonly string[] = [
  'Plans list these only where a provider had 20 or more claims for the service in a 90-day window, so most codes have none.',
  'Each distinct amount is listed once, without a claim count, and many look like totals of several claims. Compare them with each other, not with negotiated rates.',
]

export const PER_UNIT_NOTE = 'Negotiated rates for this code are per unit; these amounts are for the whole service.'

/** Within 5% reads "about the same". */
const ABOUT_THE_SAME = 0.05

/** A dollar amount in a sentence: whole dollars from $100 up ("$1,375"), cents below ("$60.12", but "$48"). */
export function dollars(value: number): string {
  const amount = Math.abs(value)
  return amount >= 100 || Math.round(amount * 100) % 100 === 0 ? wholeDollars(amount) : money(amount)
}

/** "about 4.8 times", "about 33 times": one decimal under 10, whole numbers from 10. */
export function aboutTimes(ratio: number): string {
  return `about ${ratio < 10 ? Math.round(ratio * 10) / 10 : Math.round(ratio)} times`
}

/** The two bills people know: "Doctor's fee" (professional) and "Facility fee". */
export function feeLabel(feeType: string): string {
  return feeType === 'professional' ? "Doctor's fee" : feeType === 'facility' ? 'Facility fee' : 'Fee'
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

/** Under 5 amounts, every amount; from 5 on, "Typical: $1,375" (the middle amount). */
export function amountHeadline(summary: AmountSummary): string {
  return summary.median == null ? listText(summary.listed) : `Typical: ${dollars(summary.median)}`
}

/** From 5 amounts on, "Ran from $60.12 to $187,539"; under 5 the headline already lists them all. */
export function amountRange(summary: AmountSummary): string | null {
  return summary.median == null ? null : `Ran from ${dollars(summary.min)} to ${dollars(summary.max)}`
}

/**
 * One amount set as a sentence, for layouts without columns: "Typical allowed amount: $1,375. Ran from $60.12 to
 * $187,539." or, under 5, "Allowed amounts: $218, $545, $763 and $8,374."
 */
export function amountSentence(summary: AmountSummary, noun: 'allowed amount' | 'billed charge'): string {
  if (summary.median == null) return `${noun === 'allowed amount' ? 'Allowed amounts' : 'Billed charges'}: ${listText(summary.listed)}.`
  return `Typical ${noun}: ${dollars(summary.median)}. ${amountRange(summary)}.`
}

/**
 * Billed charges against the allowed amount, from 5 pairs: "about 4.8 times", "about the same as", "less than". The
 * caller says against what. Null with too few pairs to say.
 */
export function billedVersusAllowed(group: OutOfNetworkGroup): string | null {
  const ratio = group.billedToAllowed
  if (ratio == null) return null
  if (Math.abs(ratio - 1) < ABOUT_THE_SAME) return 'about the same as'
  return ratio < 1 ? 'less than' : aboutTimes(ratio)
}

/** "Billed charges were usually about 4.8 times the allowed amount." Null with too few pairs. */
export function billedLine(group: OutOfNetworkGroup): string | null {
  const versus = billedVersusAllowed(group)
  return versus == null ? null : `Billed charges were usually ${versus} the allowed amount.`
}

/** The group a one-line reading speaks for: the one with the most payments. */
export function leadGroup(groups: readonly OutOfNetworkGroup[]): OutOfNetworkGroup | null {
  return [...groups].sort((a, b) => b.payments - a.payments)[0] ?? null
}

/** What the plan allowed, when billed charges can't be compared: the amounts, or the typical one. */
function allowedPhrase(group: OutOfNetworkGroup): string {
  return group.allowed.median == null
    ? `the plan allowed ${listText(group.allowed.listed)}`
    : `the typical allowed amount was ${dollars(group.allowed.median)}`
}

/** Plans lens: what providers billed against what the plan allowed out of network. */
export function plansLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  const versus = billedVersusAllowed(lead)
  return versus != null
    ? `Out of network (${where}), billed charges were usually ${versus} what the plan allowed, over ${plural(lead.payments, 'payment')}.`
    : `Out of network (${where}), ${allowedPhrase(lead)} over ${plural(lead.payments, 'payment')}.`
}

/** Providers lens: what providers outside the network billed against what they were allowed. */
export function providersLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  const versus = billedVersusAllowed(lead)
  if (versus != null) {
    return `Providers outside the network usually billed ${versus} what the plan allowed them (${where}, ${plural(lead.payments, 'payment')}).`
  }
  return lead.allowed.median == null
    ? `Providers outside the network were allowed ${listText(lead.allowed.listed)} (${where}).`
    : `For providers outside the network, the typical allowed amount was ${dollars(lead.allowed.median)} (${where}, ${plural(lead.payments, 'payment')}).`
}
