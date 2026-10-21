import { plural, shortDate } from '@/lib/format'
import { codeName, providerName } from '@/lib/plainNames'
import { MIN_PROVIDERS_FOR_TYPICAL, MIN_SERVICES_FOR_SCORECARD } from '@/lib/plainNumbers'
import type { ProviderScorecard, StatisticsBasis } from '../api/types'
import { versusTypical } from './plainWords'

// Short words for the insights pages (decisions 89 and 90). Pure functions of the API's numbers, so they're unit-tested.

export type Tone = 'low' | 'high' | 'neutral'

/** Green under typical, red over, plain within 5%. */
export function toneOf(ratio: number | null | undefined): Tone {
  if (ratio == null) return 'neutral'
  if (ratio <= 0.95) return 'low'
  if (ratio >= 1.05) return 'high'
  return 'neutral'
}

/**
 * A provider's overall reading for its big tile: "18% above typical", "About typical", "About 2.1 times typical", or
 * "Too few to say" with what it would take.
 */
export function overallReading(scorecard: Pick<ProviderScorecard, 'index'>): { figure: string; line: string; tone: Tone } {
  const { index } = scorecard
  if (index.value == null || index.value <= 0 || index.groupsCounted < MIN_SERVICES_FOR_SCORECARD) {
    return { figure: 'Too few to say', line: `Needs ${MIN_SERVICES_FOR_SCORECARD} services with ${MIN_PROVIDERS_FOR_TYPICAL}+ places`, tone: 'neutral' }
  }
  return { figure: versusTypical(index.value, 1) ?? 'About typical', line: `Across ${plural(index.groupsCounted, 'service')}`, tone: toneOf(index.value) }
}

/**
 * One line under a scorecard's title saying it's the provider the reader came from: "Also listed as The Trustees of
 * Columbia University in the City of NY". `cameAs` (the name on the search row that linked here) counts only when it's
 * one of the provider's own names; without it, the first other name. Null when there's nothing different to say.
 */
export function alsoListedAs(provider: Pick<ProviderScorecard['provider'], 'name' | 'otherNames'>, cameAs?: string | null): string | null {
  const key = (name: string) => providerName(name).toLowerCase()
  const line = (name: string) => `Also listed as ${providerName(name)}`
  const known = [provider.name, ...provider.otherNames]
  if (cameAs && known.some((name) => key(name) === key(cameAs))) return key(cameAs) === key(provider.name) ? null : line(cameAs)
  const other = provider.otherNames.find((name) => key(name) !== key(provider.name))
  return other ? line(other) : null
}

/** How the overall reading is worked out, for Details: true whichever way the provider leans. */
export const OVERALL_METHOD =
  "Each price is compared with that service's typical price. The reading is the middle of those comparisons, so it " +
  "doesn't count how many are above or below."

/** For Details: the facility-only charges left out of the reading, or null. */
export function facilityLeftOut(scorecard: Pick<ProviderScorecard, 'facilityCharges' | 'includeFacilityCharges'>): string | null {
  const count = scorecard.includeFacilityCharges ? 0 : scorecard.facilityCharges
  return count ? `${plural(count, 'facility-only charge')} not counted.` : null
}

/** Which plan files and when their figures were saved, for Details. */
export function statisticsLine(basis: StatisticsBasis): string {
  const files = `Plan files of ${shortDate(basis.snapshot.indexDate)}`
  return basis.builtAt ? `${files} · figures saved ${shortDate(basis.builtAt)}` : files
}

/**
 * Why the figures may not match a search, or null when they're current. It promises a refresh only while a load or a
 * rebuild is running (`rebuilding`): that's when the page refreshes by itself.
 */
export function staleMessage(basis: StatisticsBasis, rebuilding: boolean): string | null {
  if (basis.isCurrent) return null
  if (basis.builtAt) return rebuilding ? 'Figures are updating. This page refreshes when done.' : 'Figures are out of date until the next load.'
  return rebuilding ? 'Working out the figures. This page fills in when done.' : 'No figures yet. They come with the next load.'
}

/** "CPT 45378 · Colonoscopy" or "RC 0360 · Operating room", for Details and the table filter. */
export function serviceName(item: { codeType: string; code: string; codeName: string | null }): string {
  const name = codeName(item.code, item.codeName)
  return name ? `${item.codeType} ${item.code} · ${name}` : `${item.codeType} ${item.code}`
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
