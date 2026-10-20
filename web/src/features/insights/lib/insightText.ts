import { plural, shortDate, times, wholeNumber } from '@/lib/format'
import type { ProviderScorecard, StatisticsBasis } from '../api/types'

// Plain sentences for the insights pages. Pure functions of the API's numbers, so they're unit-tested.

/** "13% lower", "10% higher", "the same" for a ratio against 1 (0.87 → 13% lower). */
export function percentDifference(ratio: number): string {
  const percent = Math.round(Math.abs(ratio - 1) * 100)
  if (percent === 0) return ratio === 1 ? 'the same' : 'within 1%'
  return `${wholeNumber(percent)}% ${ratio < 1 ? 'lower' : 'higher'}`
}

/** What a provider's price index means, or why there is none. */
export function indexSentence(index: ProviderScorecard['index']): string {
  if (index.value == null) {
    return `No index: it needs ${plural(index.minimumGroups, 'group')} with ${index.minimumProviders} or more providers, and this provider has ${index.groupsCounted}.`
  }
  const percent = Math.round(Math.abs(index.value - 1) * 100)
  const where = percent === 0 ? 'at the median' : `${percent}% ${index.value < 1 ? 'below' : 'above'} the median`
  return `Typically ${where} for the same service, across ${plural(index.groupsCounted, 'group')} with ${index.minimumProviders} or more providers.`
}

/** How the index is worked out, for its tooltip. */
export function indexExplainer(value: number | null): string {
  const example = value == null ? '' : ` Half its groups sit at or below ${times(value)}, half at or above.`
  return `The median of its × median values: its rate ÷ the group's median, in every group with a median.${example}`
}

/** "Below the median in 76 groups, at it in 1, above it in 99." */
export function medianCounts(index: ProviderScorecard['index']): string {
  return `Below the median in ${plural(index.below, 'group')}, at it in ${index.atMedian}, above it in ${index.above}.`
}

/** Snapshot and build time, for the line under each insights page's title. */
export function statisticsLine(basis: StatisticsBasis): string {
  const snapshot = `Snapshot ${shortDate(basis.snapshot.indexDate)}`
  return basis.builtAt ? `${snapshot} · statistics saved ${shortDate(basis.builtAt)}` : snapshot
}

/** Why the numbers may not match a search yet, or null when they're current. */
export function staleMessage(basis: StatisticsBasis): string | null {
  if (basis.isCurrent) return null
  return basis.builtAt
    ? "These statistics are being rebuilt, so some numbers may not match a search yet. The page updates on its own when they're ready."
    : "Statistics for this snapshot are still being built. The page fills in on its own when they're ready."
}

/** "CPT 45378 · Colonoscopy" or just "RC 0360" when the plan's files don't name the code. */
export function serviceName(item: { codeType: string; code: string; codeName: string | null }): string {
  return item.codeName ? `${item.codeType} ${item.code} · ${item.codeName}` : `${item.codeType} ${item.code}`
}

/** A share as a whole percent: 5,985 of 9,369 → "64%". */
export function share(part: number, whole: number): string {
  return whole > 0 ? `${Math.round((part / whole) * 100)}%` : '—'
}
