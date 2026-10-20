import { plural, shortDate } from '@/lib/format'
import { MIN_PROVIDERS_FOR_TYPICAL, MIN_SERVICES_FOR_SCORECARD, scorecardLine } from '@/lib/plainNumbers'
import type { ProviderScorecard, StatisticsBasis } from '../api/types'

// Plain sentences for the insights pages. Pure functions of the API's numbers, so they're unit-tested.

/**
 * A provider's overall reading ("Usually about 8% more than typical (across 26 services)") or why there's none, plus a
 * line when its facility charges with no procedure named were left out of it.
 */
export function overallSummary(scorecard: Pick<ProviderScorecard, 'index' | 'facilityCharges' | 'includeFacilityCharges'>): {
  headline: string
  detail: string | null
} {
  const { index } = scorecard
  const leftOut = scorecard.includeFacilityCharges ? 0 : scorecard.facilityCharges
  const facility = leftOut
    ? `${leftOut === 1 ? 'Its 1 facility charge' : `Its ${plural(leftOut, 'facility charge')}`} with no procedure named ${leftOut === 1 ? "isn't" : "aren't"} counted.`
    : null
  const headline = scorecardLine(index.value, index.groupsCounted)
  if (headline) return { headline, detail: facility }
  const reason = `That needs ${plural(MIN_SERVICES_FOR_SCORECARD, 'service')} with ${MIN_PROVIDERS_FOR_TYPICAL} or more providers each; this provider has ${index.groupsCounted}.`
  return { headline: 'Not enough to sum up', detail: facility ? `${reason} ${facility}` : reason }
}

/** How the overall reading is worked out, for its tooltip: true whichever way the provider leans. */
export const OVERALL_EXPLAINER =
  "Each service's price is compared with the typical price for that service (what the middle provider gets). " +
  'Put those comparisons in order from lowest to highest, and this is the one in the middle. ' +
  "It doesn't count how many services are above or below typical."

/** Which plan files and when their figures were saved, for the line under each insights page's title. */
export function statisticsLine(basis: StatisticsBasis): string {
  const files = `Plan files of ${shortDate(basis.snapshot.indexDate)}`
  return basis.builtAt ? `${files} · figures saved ${shortDate(basis.builtAt)}` : files
}

/**
 * Why the figures may not match a search, or null when they're current. It promises a refresh only while a load or a
 * rebuild is running (`rebuilding`): that's when the page refreshes by itself. Otherwise they're rebuilt on the next load.
 */
export function staleMessage(basis: StatisticsBasis, rebuilding: boolean): string | null {
  if (basis.isCurrent) return null
  if (basis.builtAt) {
    return rebuilding
      ? "These saved figures are out of date, so some may not match a search until they're rebuilt. This page refreshes when that finishes."
      : "These saved figures are out of date, so some may not match a search. They'll be rebuilt the next time the data loads."
  }
  return rebuilding
    ? "The figures for these plan files haven't been saved yet. This page fills in when they are."
    : "The figures for these plan files haven't been saved yet. They'll be saved the next time the data loads."
}

/** "CPT 45378 · Colonoscopy" or "RC 0360 · Operating room"; just the code when nothing names it. */
export function serviceName(item: { codeType: string; code: string; codeName: string | null }): string {
  return item.codeName ? `${item.codeType} ${item.code} · ${item.codeName}` : `${item.codeType} ${item.code}`
}

/** A share as a whole percent: 5,985 of 9,369 → "64%". */
export function share(part: number, whole: number): string {
  if (whole <= 0) return '—'
  const percent = Math.round((part / whole) * 100)
  // A few out of thousands isn't "0%", and nearly all isn't "100%".
  if (percent === 0 && part > 0) return 'under 1%'
  if (percent === 100 && part < whole) return 'over 99%'
  return `${percent}%`
}
