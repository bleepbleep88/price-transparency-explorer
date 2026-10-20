import { buildUrl } from '@/lib/apiClient'
import { money, ordinal, plural, times, wholeNumber } from '@/lib/format'
import type { CodeInfo, RateGroup, RateSearchResult, SearchQuality } from '../api/types'
import { toApiQuery, type Lens, type SearchParams } from './searchParams'

// Audience lenses read the current search response; they never fetch anything and never change the numbers.
// Each sentence says only what the response supports.

export interface LensOption {
  key: Lens
  label: string
  /** What the explorer answers for this reader, in one line. */
  answers: string
}

export const LENS_OPTIONS: readonly LensOption[] = [
  { key: 'providers', label: 'Providers', answers: 'Where your negotiated rate sits among other providers for the same service.' },
  { key: 'plans', label: 'Plans', answers: 'How far rates spread for the same service, and where the outliers are.' },
  { key: 'employers', label: 'Employers', answers: 'What a service typically costs, and the gap to lower-priced providers.' },
  { key: 'innovators', label: 'Innovators', answers: 'How the raw files clean up, with every rate traceable to its source.' },
  { key: 'life-sciences', label: 'Life sciences', answers: 'Drug prices per billing unit, side by side by site of care.' },
]

export function lensOption(key: Lens): LensOption {
  return LENS_OPTIONS.find((option) => option.key === key)!
}

export interface ExternalLink {
  label: string
  href: string
}

export const API_REFERENCE_LINKS: readonly ExternalLink[] = [
  { label: 'API reference', href: '/scalar/v1' },
  { label: 'OpenAPI document', href: '/openapi/v1.json' },
]

export interface StartExample {
  code: string
  label: string
  npi?: string
}

export interface StartCard extends LensOption {
  examples: StartExample[]
  links?: readonly ExternalLink[]
}

/** Home page: one card per audience. Every example is a search with something to show in the 2026-08-31 files. */
export const START_HERE: readonly StartCard[] = [
  {
    ...lensOption('providers'),
    examples: [
      { code: '99213', npi: '1801474036', label: "Office visit, level 3, with one podiatry practice's NPI" },
      { code: '99214', label: 'Office visit, level 4: visit codes come in levels' },
    ],
  },
  {
    ...lensOption('plans'),
    examples: [
      { code: '0490', label: 'Ambulatory surgery facility charge (revenue code)' },
      { code: '99284', label: 'Emergency department visit, level 4' },
    ],
  },
  {
    ...lensOption('employers'),
    examples: [
      { code: '45378', label: 'Colonoscopy' },
      { code: '66984', label: 'Cataract surgery' },
    ],
  },
  {
    ...lensOption('innovators'),
    examples: [
      { code: '29881', label: 'Knee arthroscopy: published only as a facility rate under revenue code 0490' },
      { code: '812', label: 'Colonoscopy anesthesia: published without its leading zeros' },
    ],
    links: API_REFERENCE_LINKS,
  },
  {
    ...lensOption('life-sciences'),
    examples: [
      { code: 'J0585', label: 'Botulinum toxin, per unit' },
      { code: 'J1100', label: 'Dexamethasone, per 1 mg' },
    ],
  },
]

/** "Ambetter from Fidelis Care · Facility fee · Ambulatory surgery center (TC)" */
export function groupName(group: RateGroup): string {
  const modifiers = group.modifiers.length ? ` (${group.modifiers.map((modifier) => modifier.code).join(', ')})` : ''
  return `${group.productLabel} · ${group.feeTypeLabel} · ${group.settingLabel}${modifiers}`
}

function sentence(parts: string[]): string {
  const text = parts.join('; ')
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`
}

// ---- Providers ----

export function hasProviderFilter(result: RateSearchResult): boolean {
  const query = result.query
  return Boolean(query.npi || query.ein || query.provider)
}

/** An NPI or EIN pins one provider, so the reading can say "your". A name filter can match many providers. */
export function pinsProvider(result: RateSearchResult): boolean {
  return Boolean(result.query.npi || result.query.ein)
}

/** At least half the group is paid exactly the median (25th and 75th percentiles equal it), so it is the most common rate. */
export function isMostCommonRate(group: RateGroup, rate: number): boolean {
  const { p25, median, p75 } = group.stats
  return median != null && rate === median && p25 === median && p75 === median
}

export interface YourRateLine {
  groupKey: string
  providerKey: string
  group: string
  provider: string
  rate: number
  text: string
}

/** For each group the searched provider appears in: where its rate sits. */
export function yourRateLines(result: RateSearchResult): YourRateLine[] {
  const yours = pinsProvider(result)
  const subject = yours ? 'Your rate' : 'Rate'
  const rows = new Map(result.rows.items.map((row) => [`${row.groupKey}|${row.providerKey}`, row]))
  return result.groups.flatMap((group) =>
    group.highlights.map((position) => {
      let text: string
      if (group.statsLevel === 'list') {
        text = `${subject} ${money(position.rate)}. With ${plural(group.providerCount, 'provider')} in this group there is no percentile.`
      } else if (position.percentile != null && position.vsMedian != null) {
        const common = isMostCommonRate(group, position.rate) ? ' (the most common rate)' : ''
        text = `${subject} ${money(position.rate)} is at the ${ordinal(position.percentile)} percentile, ${times(position.vsMedian)} the median${common}.`
      } else if (position.vsMedian != null) {
        text = `${subject} ${money(position.rate)} is ${times(position.vsMedian)} the median.`
      } else {
        text = `${subject} ${money(position.rate)}.`
      }
      // Several rates in one group: the table row (when it's on this page) has the range.
      const row = rows.get(`${group.key}|${position.providerKey}`)
      if (row?.multipleRates) {
        text = `${text.slice(0, -1)} (${yours ? 'your rates here' : 'its rates'} range from ${money(row.rateLow)} to ${money(row.rateHigh)}).`
      }
      return {
        groupKey: group.key,
        providerKey: position.providerKey,
        group: groupName(group),
        provider: position.displayName,
        rate: position.rate,
        text,
      }
    }),
  )
}

/** Heading over a card's highlights: "your" only when an NPI or EIN pins one provider. */
export function highlightHeading(result: RateSearchResult): string {
  return pinsProvider(result) || !result.query.provider
    ? 'Where your provider sits'
    : `Where providers whose name contains "${result.query.provider}" sit`
}

// ---- Plans ----

export interface SpreadLine {
  groupKey: string
  group: string
  providers: number
  median: number
  /** Full statistics only (10+ providers). */
  p90ToP10: number | null
  highestToMedian: number
  gap: number | null
}

export function spreadLines(result: RateSearchResult): SpreadLine[] {
  return result.groups
    .filter((group) => group.statsLevel !== 'list' && group.stats.median != null && group.stats.median > 0)
    .map((group) => ({
      groupKey: group.key,
      group: groupName(group),
      providers: group.providerCount,
      median: group.stats.median!,
      p90ToP10: group.stats.p90ToP10,
      highestToMedian: group.stats.max / group.stats.median!,
      gap: group.lowerQuartileGap,
    }))
}

/** Groups whose 90th percentile is above their 10th, widest first. A group where they're equal has no spread to show. */
export function widestSpread(lines: SpreadLine[], count = 3): SpreadLine[] {
  return lines
    .filter((line) => line.p90ToP10 != null && line.p90ToP10 > 1)
    .sort((a, b) => b.p90ToP10! - a.p90ToP10! || b.providers - a.providers)
    .slice(0, count)
}

export function spreadText(line: SpreadLine): string {
  const parts: string[] = []
  if (line.p90ToP10 != null) parts.push(`the 90th percentile is ${times(line.p90ToP10)} the 10th`)
  parts.push(`the highest rate is ${times(line.highestToMedian)} the median`)
  if (line.gap) parts.push(`the gap to the lower quartile is ${money(line.gap)}`)
  return sentence(parts)
}

// ---- Employers ----

export interface CostLine {
  groupKey: string
  group: string
  providers: number
  median: number
  /** Gap to the lower quartile (full statistics only): per case, or per unit when perUnit. */
  gap: number | null
  perUnit: boolean
  /** gap × cases a year. Never for per-unit prices: a case can use several units. */
  yearly: number | null
}

export function costLines(result: RateSearchResult, cases?: number | null): CostLine[] {
  return result.groups
    .filter((group) => group.stats.median != null)
    .map((group) => ({
      groupKey: group.key,
      group: groupName(group),
      providers: group.providerCount,
      median: group.stats.median!,
      gap: group.lowerQuartileGap,
      perUnit: group.perUnit,
      yearly: !group.perUnit && group.lowerQuartileGap != null && cases ? group.lowerQuartileGap * cases : null,
    }))
    .sort((a, b) => b.providers - a.providers)
}

/**
 * The group the yearly sentence speaks for: the one the reader picked ("Show these providers") when it has a gap,
 * else the largest group with a gap above zero. A zero gap has nothing to multiply.
 */
export function yearlyLead(lines: CostLine[], pickedGroup?: string | null): CostLine | null {
  const hasYearly = (line: CostLine) => line.yearly != null && line.gap != null && line.gap > 0
  const picked = pickedGroup ? lines.find((line) => line.groupKey === pickedGroup) : undefined
  if (picked && hasYearly(picked)) return picked
  return lines.find(hasYearly) ?? null
}

export const EMPLOYER_CAVEATS: readonly string[] = [
  "These are individual-market plans (Ambetter, Essential Plan), not employer plans; an employer's own rates can differ.",
  "A negotiated rate isn't what a member pays; the plan's cost sharing decides that.",
  'The files have no claim counts, so the case count is yours.',
  'Facility and professional fees are separate bills for the same visit.',
]

// ---- Innovators ----

export function qualityLines(quality: SearchQuality): string[] {
  return [
    `${wholeNumber(quality.publishedRows)} published rows became ${plural(quality.uniqueRates, 'unique rate')}.`,
    `${plural(quality.duplicatesCollapsed, 'exact duplicate')} collapsed.`,
    `${plural(quality.maskedTaxIds, 'tax ID')} masked: prefixes the IRS never issues.`,
    `${plural(quality.wholeTaxIdContracts, 'whole-tax-ID contract')}: no individual NPIs listed.`,
  ]
}

/** The API request behind this exact search, as an absolute URL to paste into a terminal or notebook. */
export function apiSearchUrl(params: SearchParams, origin: string): string {
  return new URL(buildUrl('/api/v1/rates', toApiQuery(params)), origin).href
}

// ---- Life sciences ----

/** The API's category decides; the code's letters don't (Q0091 is a procedure, 90715 a vaccine). */
export function isDrugCode(code: CodeInfo | null | undefined): boolean {
  return code?.category.key === 'drugs'
}

/** Rates the plan files under a revenue code (the drug named only in a notes field): a facility charge, not a unit price. */
export function filedUnderRevenueCode(group: RateGroup): boolean {
  return group.codeType !== 'RC' && group.billedAs.some((billed) => billed.billingCodeType === 'RC')
}

export function hasFacilityCharges(result: RateSearchResult): boolean {
  return result.groups.some(filedUnderRevenueCode)
}

export interface UnitPriceLine {
  groupKey: string
  product: string
  /** Setting, fee type and modifiers: "Doctor's office · Professional (clinician) fee (JW)". */
  site: string
  providers: number
  kind: 'median' | 'single' | 'range'
  price: number | null
  low: number | null
  high: number | null
}

/**
 * Per-unit prices by site of care, ordered by setting then fee type so the same drug reads side by side.
 * Only groups priced per unit and billed under the code itself: revenue-code facility charges aren't unit prices.
 */
export function unitPriceLines(result: RateSearchResult): UnitPriceLine[] {
  return result.groups
    .filter((group) => group.perUnit && !filedUnderRevenueCode(group))
    .flatMap((group): UnitPriceLine[] => {
      const modifiers = group.modifiers.length ? ` (${group.modifiers.map((modifier) => modifier.code).join(', ')})` : ''
      const base = {
        groupKey: group.key,
        product: group.productLabel,
        site: `${group.settingLabel} · ${group.feeTypeLabel}${modifiers}`,
        providers: group.providerCount,
      }
      if (group.stats.median != null) {
        return [{ ...base, kind: 'median', price: group.stats.median, low: group.stats.min, high: group.stats.max }]
      }
      const listed = group.listedRates
      if (listed.length === 1) return [{ ...base, kind: 'single', price: listed[0]!, low: null, high: null }]
      if (listed.length > 1) return [{ ...base, kind: 'range', price: null, low: Math.min(...listed), high: Math.max(...listed) }]
      return []
    })
    .sort((a, b) => a.site.localeCompare(b.site) || a.product.localeCompare(b.product))
}

export function unitPriceText(line: UnitPriceLine): string {
  if (line.kind === 'median') return `${money(line.price)} median`
  if (line.kind === 'single') return `${money(line.price)}, one rate`
  return line.low === line.high ? money(line.low) : `${money(line.low)} to ${money(line.high)}`
}

export const DRUG_EXAMPLES: readonly StartExample[] = START_HERE.find((card) => card.key === 'life-sciences')!.examples
