import { money, plural, wholeDollars } from './format'

// Plain words for every number the UI shows (decision 89). The API keeps its precise fields (median, percentiles,
// ratios); these functions turn them into short sentences that claim only what the plan's files support:
// - "typical" is the middle provider's price (the median), and only from 5 providers;
// - "most providers" is the middle 80% (10th to 90th percentile), and only from 10 providers;
// - under 5 providers there is no typical price: show the prices themselves.
// Never "percentile", "median", "quartile", "×" or "index".

/** Fewer providers than this: no typical price, no position, just the prices. */
export const MIN_PROVIDERS_FOR_TYPICAL = 5
/** "Most providers get between", the lower-priced quarter and how much prices vary need this many. */
export const MIN_PROVIDERS_FOR_RANGE = 10
/** A provider's overall reading needs this many services in common with others. */
export const MIN_SERVICES_FOR_SCORECARD = 5
/** Within 5% of typical reads "about the same as typical". */
export const ABOUT_THE_SAME = 0.05
/** From twice the typical price up, say "times" instead of dollars. */
export const TIMES_FROM = 2

export type Band = 'low' | 'typical' | 'high'

/** What the lines need about one service in one setting and plan: one price per provider, summed up. */
export interface PriceSummary {
  providers: number
  min: number
  max: number
  p10: number | null
  p25: number | null
  median: number | null
  p75: number | null
  p90: number | null
}

export function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}

export function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1)
}

/** A dollar amount in a sentence, sign dropped: whole dollars from $100 up ("$454"), cents below ("$12.34", "$0.17"). */
export function dollars(value: number): string {
  const amount = Math.abs(value)
  return amount >= 100 ? wholeDollars(amount) : money(amount)
}

/** An estimate, rounded to three figures so it doesn't claim precision: 148,632 → "$149,000". */
export function aboutDollars(value: number): string {
  const amount = Math.abs(value)
  if (amount < 1000) return dollars(amount)
  const step = 10 ** (Math.floor(Math.log10(amount)) - 2)
  return wholeDollars(Math.round(amount / step) * step)
}

/** "about 2.4 times", "about 6 times", "about 20 times": one decimal under 10, whole numbers from 10. */
export function aboutTimes(ratio: number): string {
  const rounded = ratio < 10 ? Math.round(ratio * 10) / 10 : Math.round(ratio)
  return `about ${rounded} times`
}

/** The typical price (the middle provider's), or null under 5 providers. */
export function typicalPrice(summary: PriceSummary): number | null {
  return summary.providers >= MIN_PROVIDERS_FOR_TYPICAL ? summary.median : null
}

/** "Typical price: $578", or "Typical price: $0.17 per unit". Null under 5 providers. */
export function typicalLine(summary: PriceSummary, unit?: string): string | null {
  const typical = typicalPrice(summary)
  return typical == null ? null : `Typical price: ${dollars(typical)}${unit ? ` ${unit}` : ''}`
}

/**
 * How far prices run. 10+ providers: "Most providers get between $372 and $664." (the middle 80%). 5 to 9: "Prices run
 * from $185 to $205." Under 5: null, so the caller lists the prices instead.
 */
export function rangeLine(summary: PriceSummary): string | null {
  if (summary.providers >= MIN_PROVIDERS_FOR_RANGE && summary.p10 != null && summary.p90 != null) {
    const [low, high] = [dollars(summary.p10), dollars(summary.p90)]
    return low === high ? `Most providers get ${low}.` : `Most providers get between ${low} and ${high}.`
  }
  if (summary.providers >= MIN_PROVIDERS_FOR_TYPICAL) {
    const [low, high] = [dollars(summary.min), dollars(summary.max)]
    return low === high ? `Every provider gets ${low}.` : `Prices run from ${low} to ${high}.`
  }
  return null
}

/** Under 5 providers there is nothing to compare: "Only 3 providers: too few to compare." */
export function fewProvidersLine(providers: number): string {
  return `Only ${plural(providers, 'provider')}: too few to compare.`
}

const POSITION_LABELS: Record<Band, string> = {
  low: 'Lower than most',
  typical: 'About typical',
  high: 'Higher than most',
}

/** The API's band in words: "Lower than most", "About typical", "Higher than most". Null without one (under 5 providers). */
export function positionLabel(band: string | null | undefined): string | null {
  return band && band in POSITION_LABELS ? POSITION_LABELS[band as Band] : null
}

/**
 * The band for a price the API didn't band (a card's highlighted provider). Same rule as the API: low at or under the
 * lower quarter and under typical, high at or over the upper quarter and over typical. Null under 5 providers.
 */
export function bandFor(rate: number, summary: PriceSummary): Band | null {
  const { p25, median, p75 } = summary
  if (summary.providers < MIN_PROVIDERS_FOR_TYPICAL || p25 == null || median == null || p75 == null) return null
  if (rate <= p25 && rate < median) return 'low'
  if (rate >= p75 && rate > median) return 'high'
  return 'typical'
}

/**
 * A price against the typical price: "$12 less than typical", "about the same as typical" (within 5%), "$29 more than
 * typical", or "about 2.4 times the typical price" from twice up. Null without a typical price.
 */
export function differenceLine(rate: number, typical: number | null | undefined): string | null {
  if (typical == null || typical <= 0) return null
  const ratio = rate / typical
  if (Math.abs(ratio - 1) < ABOUT_THE_SAME) return 'about the same as typical'
  if (ratio >= TIMES_FROM) return `${aboutTimes(ratio)} the typical price`
  return `${dollars(rate - typical)} ${rate < typical ? 'less' : 'more'} than typical`
}

/**
 * Position and dollars together: "Lower than most · $23 less than typical", "Higher than most · about 2.7 times the
 * typical price". "About typical" stands alone when the dollars say the same thing. Null under 5 providers.
 */
export function positionLine(band: string | null | undefined, rate: number, typical: number | null | undefined, separator = ' · '): string | null {
  const label = positionLabel(band)
  const difference = differenceLine(rate, typical)
  if (!label) return difference ? capitalize(difference) : null
  if (!difference || (band === 'typical' && difference === 'about the same as typical')) return label
  return `${label}${separator}${difference}`
}

/** "The lower-priced quarter of providers get $454 or less: $124 less than typical." 10+ providers only. */
export function gapLine(summary: PriceSummary): string | null {
  const { p25, median } = summary
  if (summary.providers < MIN_PROVIDERS_FOR_RANGE || p25 == null || median == null) return null
  const gap = median - p25
  return gap > 0
    ? `The lower-priced quarter of providers get ${dollars(p25)} or less: ${dollars(gap)} less than typical.`
    : `The lower-priced quarter of providers get ${dollars(p25)} or less, which is also the typical price.`
}

/** "$124 less per case" ("per unit" for drugs and other per-unit prices); "No less" when there's no gap. */
export function lessPerCase(gap: number, perUnit = false): string {
  return gap > 0 ? `${dollars(gap)} less per ${perUnit ? 'unit' : 'case'}` : 'No less'
}

/** For plans and employers: "$124 less per case at a lower-priced provider." Null without a gap (under 10 providers). */
export function perCaseLine(gap: number | null | undefined, perUnit = false): string | null {
  if (gap == null) return null
  return gap > 0 ? `${lessPerCase(gap, perUnit)} at a lower-priced provider.` : `No less per ${perUnit ? 'unit' : 'case'} at a lower-priced provider.`
}

/**
 * The yearly figure, only from the reader's own case count (the files don't say how often a service happens):
 * "About $149,000 less a year for your 1,200 cases at lower-priced providers." Null without cases or a gap.
 */
export function yearlyLine(gap: number | null | undefined, cases: number | null | undefined): string | null {
  if (gap == null || gap <= 0 || !cases) return null
  return `About ${aboutDollars(gap * cases)} less a year for your ${plural(cases, 'case')} at lower-priced providers.`
}

/**
 * How much prices vary, leaving out the top and bottom tenth: "Even leaving out the extremes, the high end is about 6
 * times the low end." 10+ providers only.
 */
export function spreadLine(summary: PriceSummary): string | null {
  const { p10, p90 } = summary
  if (summary.providers < MIN_PROVIDERS_FOR_RANGE || p10 == null || p90 == null || p10 <= 0) return null
  const ratio = p90 / p10
  if (ratio - 1 < ABOUT_THE_SAME) return 'Even leaving out the extremes, most providers get about the same price.'
  return `Even leaving out the extremes, the high end is ${aboutTimes(ratio)} the low end.`
}

/**
 * A provider's overall reading across the services it shares with others, from the typical ratio of its price to each
 * service's typical price: "Usually about 8% more than typical (across 26 services)", "About typical (across 26
 * services)", "Usually about 12% less than typical (across 26 services)". Null under 5 services.
 */
export function scorecardLine(ratio: number | null | undefined, services: number): string | null {
  if (ratio == null || ratio <= 0 || services < MIN_SERVICES_FOR_SCORECARD) return null
  const across = `(across ${plural(services, 'service')})`
  if (Math.abs(ratio - 1) < ABOUT_THE_SAME) return `About typical ${across}`
  if (ratio >= TIMES_FROM) return `Usually ${aboutTimes(ratio)} the typical price ${across}`
  return `Usually about ${Math.round(Math.abs(ratio - 1) * 100)}% ${ratio < 1 ? 'less' : 'more'} than typical ${across}`
}

/** The two bills people know: "Doctor's fee" (the API's professional fee) and "Facility fee". */
export function feeLabel(feeType: string): string {
  return feeType === 'professional' ? "Doctor's fee" : feeType === 'facility' ? 'Facility fee' : 'Fee'
}

/** Each card and row compares like with like. Said once above the cards. */
export const LIKE_FOR_LIKE = 'Same service, same setting, same plan'

/** Said once per page, in the footer. */
export const ALWAYS_TRUE =
  "These are negotiated rates from the plan's files, not what a person pays. The files don't say how often a service happens."
