import { plural, shortDate } from '@/lib/format'
import type { ProviderScorecard, ScorecardGroup, StatisticsBasis } from '../api/types'
import { overallText } from './plainWords'

// Plain sentences for the insights pages. Pure functions of the API's numbers, so they're unit-tested.

/** A provider's overall reading and what it rests on, or why there's none. */
export function overallSummary(index: ProviderScorecard['index']): { headline: string; detail: string } {
  if (index.value == null) {
    return {
      headline: 'Not enough to sum up',
      detail: `That needs ${plural(index.minimumGroups, 'service')} with ${index.minimumProviders} or more providers each; this provider has ${index.groupsCounted}.`,
    }
  }
  return {
    headline: overallText(index.value),
    detail: `Across ${plural(index.groupsCounted, 'service')} with ${index.minimumProviders} or more providers.`,
  }
}

/** How the overall reading is worked out, for its tooltip. */
export const OVERALL_EXPLAINER =
  "Each service's price is compared with the typical price for that service (what the middle provider gets). " +
  'This is the middle of those comparisons: about half its services sit higher, half lower.'

/** "Lower than most for 9, about typical for 10, higher than most for 7." Groups too small to compare are left out. */
export function positionCounts(groups: readonly Pick<ScorecardGroup, 'band'>[]): string | null {
  const count = (band: string) => groups.filter((group) => group.band === band).length
  const [low, typical, high] = [count('low'), count('typical'), count('high')]
  if (low + typical + high === 0) return null
  return `Lower than most for ${low}, about typical for ${typical}, higher than most for ${high}.`
}

/** Which plan files and when their figures were saved, for the line under each insights page's title. */
export function statisticsLine(basis: StatisticsBasis): string {
  const files = `Plan files of ${shortDate(basis.snapshot.indexDate)}`
  return basis.builtAt ? `${files} · figures saved ${shortDate(basis.builtAt)}` : files
}

/**
 * Why the figures may not match a search, or null when they're current. Out of date means the data changed since they
 * were saved (a reload, a rate past its end date); the page refreshes by itself when a rebuild finishes.
 */
export function staleMessage(basis: StatisticsBasis): string | null {
  if (basis.isCurrent) return null
  return basis.builtAt
    ? "These saved figures are out of date, so some may not match a search until they're rebuilt. This page refreshes when that finishes."
    : "The figures for these plan files haven't been saved yet. This page fills in when they are."
}

/** "CPT 45378 · Colonoscopy" or "RC 0360 · Operating room"; just the code when nothing names it. */
export function serviceName(item: { codeType: string; code: string; codeName: string | null }): string {
  return item.codeName ? `${item.codeType} ${item.code} · ${item.codeName}` : `${item.codeType} ${item.code}`
}

/** A share as a whole percent: 5,985 of 9,369 → "64%". */
export function share(part: number, whole: number): string {
  return whole > 0 ? `${Math.round((part / whole) * 100)}%` : '—'
}

/** Said once on each insights page: what these numbers are and what the files don't say. */
export const RATES_NOT_COSTS =
  "These are negotiated rates in the plan's files, not what a person pays. The files don't say how often a service happens, so differences are per visit or service, never yearly totals."
