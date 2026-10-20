import type { RouteLocationRaw } from 'vue-router'
import type { RateGroup, RateSearchResult } from '@/features/rates/api/types'
import type { GapSort, SearchLink } from '../api/types'

// Links from a search's audience lenses to the insights pages, and from the insights back to search. Each link only
// appears when the search result shows the page will have something for it.

/** The searched code the way the insights filters read it ("CPT 45378", "RC 0490"), so a short number isn't ambiguous. */
export function codeFilter(result: RateSearchResult): string {
  return result.code ? `${result.code.codeType} ${result.code.code}` : result.query.code
}

function snapshotQuery(result: RateSearchResult): Record<string, string> {
  return result.snapshot.isLatest ? {} : { snapshot: String(result.snapshot.snapshotId) }
}

/** True when a group in the result has 10+ providers, so the gap leaderboard ranks it. */
export function hasRankedGroup(result: RateSearchResult): boolean {
  return result.groups.some((group) => group.statsLevel === 'full')
}

/**
 * The gap leaderboard for this search's code, under the same product, setting and fee type, when one of its groups is
 * ranked there; otherwise the whole leaderboard.
 */
export function gapsLink(result: RateSearchResult, sort: GapSort): RouteLocationRaw {
  const query: Record<string, string> = { ...snapshotQuery(result) }
  if (hasRankedGroup(result)) {
    query.code = codeFilter(result)
    // A revenue code searched on its own is a facility charge with no procedure named: ranked only when asked for.
    if (result.code?.codeType === 'RC') query.includeFacilityCharges = 'true'
    for (const key of ['product', 'setting', 'feeType'] as const) {
      const value = result.query[key]
      if (value) query[key] = value
    }
  }
  if (sort !== 'gap') query.sort = sort
  return { name: 'gaps', query }
}

/** Everything in a group's key but the product: the same service, fee type, setting, modifiers and rate type. */
function withoutProduct(group: RateGroup): string {
  return [group.codeType, group.code, group.feeType, group.settingKey, group.modifiers.map((m) => m.code).join(','), group.negotiatedType].join('|')
}

/** The product comparison for this code, when two products have the same group for it in the result. */
export function compareLink(result: RateSearchResult): RouteLocationRaw | null {
  const products = new Map<string, Set<string>>()
  for (const group of result.groups) {
    const key = withoutProduct(group)
    products.set(key, (products.get(key) ?? new Set()).add(group.productKey))
  }
  if (![...products.values()].some((keys) => keys.size >= 2)) return null
  const query: Record<string, string> = { ...snapshotQuery(result), code: codeFilter(result) }
  if (result.query.setting) query.setting = result.query.setting
  return { name: 'compare', query }
}

export interface ScorecardLink {
  to: RouteLocationRaw
  name: string
  /** The search named an NPI: the scorecard covers the whole tax ID it bills under, not only that NPI. */
  byNpi: boolean
}

/** The scorecard of the one provider (tax ID) an NPI or EIN picked out, when exactly one matched. */
export function scorecardLink(result: RateSearchResult): ScorecardLink | null {
  if (!result.query.npi && !result.query.ein) return null
  const matched = result.groups.filter((group) => group.highlights.length)
  if (matched.some((group) => group.matchedProviders > group.highlights.length)) return null
  const keys = new Set(matched.flatMap((group) => group.highlights.map((position) => position.providerKey)))
  if (keys.size !== 1) return null
  const [key] = keys
  return {
    to: { name: 'scorecard', params: { key }, query: snapshotQuery(result) },
    name: matched[0]!.highlights[0]!.displayName,
    byNpi: Boolean(result.query.npi),
  }
}

/** The search page for an insight's link (its code, code type, group and, for a provider, its key). */
export function searchLocation(link: SearchLink): RouteLocationRaw {
  return { name: 'search', query: { ...link } }
}
