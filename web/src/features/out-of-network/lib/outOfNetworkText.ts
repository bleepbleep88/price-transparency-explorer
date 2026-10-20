import { money, plural } from '@/lib/format'
import { modifierText } from '@/features/rates/lib/lenses'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { AmountSummary, OutOfNetworkGroup, OutOfNetworkResult } from '../api/types'

// Plain readings of the out-of-network answer. They only restate what the API returned.

export const OUT_OF_NETWORK_NOTES: readonly string[] = [
  'Plans list these only where a provider had 20 or more claims for the service in a 90-day window, so most codes have none.',
  'Each distinct amount is listed once, without a claim count, and many look like totals of several claims. Compare them with each other, not with negotiated rates.',
]

export const PER_UNIT_NOTE = 'Negotiated rates for this code are per unit; these amounts are for the whole service.'

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
  return `${group.productLabel} · ${group.feeTypeLabel}${code}${modifiers}`
}

/** True when the groups hold more than one kind of code ("450" can be RC 0450 and CPT 00450). */
export function severalCodes(groups: readonly OutOfNetworkGroup[]): boolean {
  return new Set(groups.map((group) => `${group.codeType} ${group.code}`)).size > 1
}

/** "$217.92, $544.80 and $762.72" */
export function listText(amounts: readonly number[]): string {
  const shown = amounts.map(money)
  return shown.length <= 2 ? shown.join(' and ') : `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}`
}

/** Under 5 amounts, every amount; from 5 on, the median. */
export function amountHeadline(summary: AmountSummary): string {
  return summary.median == null ? listText(summary.listed) : `median ${money(summary.median)}`
}

/** From 5 amounts on, "$60.12 to $187,538.55"; under 5 the headline already lists them all. */
export function amountRange(summary: AmountSummary): string | null {
  return summary.median == null ? null : `${money(summary.min)} to ${money(summary.max)}`
}

/** 0.21 → "21%" */
export function percent(share: number): string {
  return `${Math.round(share * 100)}%`
}

/** The group a one-line reading speaks for: the one with the most payments. */
export function leadGroup(groups: readonly OutOfNetworkGroup[]): OutOfNetworkGroup | null {
  return [...groups].sort((a, b) => b.payments - a.payments)[0] ?? null
}

/** Plans lens: what the plan allowed out of network, as a share of the charge when there are enough pairs. */
export function plansLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  return lead.allowedShareOfBilled != null
    ? `Out of network (${where}), the plan allowed a median ${percent(lead.allowedShareOfBilled)} of what providers billed, over ${plural(lead.payments, 'payment')}.`
    : `Out of network (${where}), the plan listed ${plural(lead.payments, 'payment')}: ${listText(lead.allowed.listed)}.`
}

/** Providers lens: what providers outside the network were allowed against what they billed. */
export function providersLensLine(groups: readonly OutOfNetworkGroup[]): string | null {
  const lead = leadGroup(groups)
  if (!lead) return null
  const where = groupLabel(lead, severalCodes(groups))
  return lead.allowedShareOfBilled != null
    ? `Providers outside the network were allowed a median ${percent(lead.allowedShareOfBilled)} of what they billed (${where}, ${plural(lead.payments, 'payment')}).`
    : `Providers outside the network were allowed ${listText(lead.allowed.listed)} (${where}).`
}
