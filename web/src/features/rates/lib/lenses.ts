import { buildUrl } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import {
  ABOUT_THE_SAME,
  bandFor,
  dollars,
  feeLabel,
  fewProvidersLine,
  gapOf,
  lowerFirst,
  MIN_PROVIDERS_FOR_RANGE,
  perCaseLine,
  positionLine,
  spreadLine,
  typicalLine,
  typicalPrice,
  yearlyLine,
  type PriceSummary,
} from '@/lib/plainNumbers'
import type { CodeInfo, ModifierRef, RateGroup, RateSearchEcho, RateSearchResult, SearchQuality } from '../api/types'
import { toApiQuery, type Lens, type SearchParams } from './searchParams'

// Audience lenses read the current search response; they never fetch anything and never change the numbers.
// Each sentence says only what the response supports, in the plain words of @/lib/plainNumbers.

export interface LensOption {
  key: Lens
  label: string
  /** What the explorer answers for this reader, in one line. */
  answers: string
}

export const LENS_OPTIONS: readonly LensOption[] = [
  { key: 'providers', label: 'Providers', answers: 'How your price compares with other providers for the same service.' },
  { key: 'plans', label: 'Plans', answers: 'How much prices vary for the same service.' },
  { key: 'employers', label: 'Employers', answers: 'What a service typically costs, and how much less lower-priced providers get.' },
  { key: 'innovators', label: 'Innovators', answers: "How the plan's raw files are cleaned up, with every price traced to its source." },
  { key: 'life-sciences', label: 'Life sciences', answers: 'Drug prices per unit, side by side by site of care.' },
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
      { code: '99213', npi: '1801474036', label: "Office visit, level 3, with one podiatrist's NPI" },
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
      { code: '812', label: 'Colonoscopy anesthesia: published as 812, searched as 00812' },
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

/**
 * Modifiers named as modifiers, never as bare numbers that read like counts: "Preventive service (modifier 33)".
 * A code the API doesn't know comes back labelled "Modifier Q1"; that reads "modifier Q1".
 */
export function modifierText(modifiers: readonly ModifierRef[]): string {
  return modifiers
    .map((modifier) => (modifier.label === `Modifier ${modifier.code}` ? `modifier ${modifier.code}` : `${modifier.label} (modifier ${modifier.code})`))
    .join(', ')
}

/** "Ambetter from Fidelis Care · Facility fee · Ambulatory surgery center · Technical component (modifier TC)" */
export function groupName(group: Pick<RateGroup, 'productLabel' | 'feeTypeLabel' | 'settingLabel' | 'modifiers'>): string {
  const modifiers = group.modifiers.length ? ` · ${modifierText(group.modifiers)}` : ''
  return `${group.productLabel} · ${feeLabel(group.feeType)} · ${group.settingLabel}${modifiers}`
}

/** A group's numbers in the shape the plain-words helpers read. */
export function priceSummary(group: RateGroup): PriceSummary {
  return { ...group.stats, providers: group.providerCount }
}

// ---- Providers ----

/** Any filter that picks providers: NPI, EIN, name, specialty or location. Groups then highlight the matches. */
export function hasProviderFilter(result: RateSearchResult): boolean {
  const query = result.query
  return Boolean(query.npi || query.ein || query.provider || query.specialty || query.county)
}

/** A specialty or location filter: it picks the rows, while typical prices still count every provider. */
export function hasDirectoryFilter(result: RateSearchResult): boolean {
  return Boolean(result.query.specialty || result.query.county)
}

/**
 * The search names a provider (NPI, EIN or name): only then do its matches get the copper "your provider" mark. A
 * specialty or location picks a set of providers, not yours.
 */
export function marksSearchedProvider(result: RateSearchResult): boolean {
  const query = result.query
  return Boolean(query.npi || query.ein || query.provider)
}

/** The place the location filter matched, as the API names it ("Delaware County"), else the text as typed, quoted. */
export function placePhrase(query: RateSearchEcho): string {
  return query.countyPlace ?? `"${query.county}"`
}

/**
 * The providers a name, specialty or location filter picks, worded like the API's hints:
 * 'providers whose name contains "heel" with a specialty matching "podiatr" in Brooklyn'.
 */
export function filteredProviders(query: RateSearchEcho): string {
  let text = query.provider ? `providers whose name contains "${query.provider}"` : 'providers'
  if (query.specialty) text += ` with a specialty matching "${query.specialty}"`
  if (query.county) text += ` in ${placePhrase(query)}`
  return text
}

/**
 * Under a specialty or location filter the rows narrow, but each group's typical price and spread still count every
 * provider in it: "Typical prices are for all of New York, not just Manhattan." Null without those filters.
 */
export function marketWideNote(query: RateSearchEcho): string | null {
  if (!query.specialty && !query.county) return null
  const place = query.county ? placePhrase(query) : null
  const who = query.specialty ? `providers with a specialty matching "${query.specialty}"${place ? ` in ${place}` : ''}` : place
  return `Typical prices are for all of New York, not just ${who}.`
}

/** An NPI or EIN pins one provider, so the reading can say "your". A name filter can match many providers. */
export function pinsProvider(result: RateSearchResult): boolean {
  return Boolean(result.query.npi || result.query.ein)
}

/** At least half the group is paid exactly the typical price (the quarter marks equal it), so it is the most common price. */
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

/** For each group the searched provider appears in: how its price compares, in words and dollars. */
export function yourRateLines(result: RateSearchResult): YourRateLine[] {
  const yours = pinsProvider(result)
  const subject = yours ? 'Your price' : 'Price'
  return result.groups.flatMap((group) =>
    group.highlights.map((position) => {
      const summary = priceSummary(group)
      const typical = typicalPrice(summary)
      let text: string
      if (typical == null) {
        text = `${subject} ${money(position.rate)}. ${fewProvidersLine(group.providerCount)}`
      } else {
        const compared = positionLine(bandFor(position.rate, summary), position.rate, typical, ', ') ?? ''
        const common = isMostCommonRate(group, position.rate) ? ', and the most common price' : ''
        text = `${subject} ${money(position.rate)}: ${lowerFirst(compared)}${common}. ${typicalLine(summary)}.`
      }
      // Several prices in one group: say the range, so one price doesn't read as the only one.
      if (position.rateHigh !== position.rateLow) {
        text = `${text} ${yours ? 'Your prices' : 'Its prices'} here run from ${money(position.rateLow)} to ${money(position.rateHigh)}.`
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

export interface YourRateGroup {
  groupKey: string
  group: string
  lines: YourRateLine[]
  /** Set when the API listed only the first matches: "Showing 5 of 12 matching providers; the table lists them all." */
  capNote: string | null
}

/** The lines grouped by card (same service, setting and plan), each saying when it shows only some of its matches. */
export function yourRateGroups(result: RateSearchResult): YourRateGroup[] {
  const lines = yourRateLines(result)
  return result.groups
    .filter((group) => group.highlights.length)
    .map((group) => ({
      groupKey: group.key,
      group: groupName(group),
      lines: lines.filter((line) => line.groupKey === group.key),
      capNote:
        group.matchedProviders > group.highlights.length
          ? `Showing ${group.highlights.length} of ${group.matchedProviders} matching providers; the table lists them all.`
          : null,
    }))
}

/** Heading over a card's highlights: "your" only when an NPI or EIN pins one provider; otherwise it names the filters. */
export function highlightHeading(result: RateSearchResult): string {
  return pinsProvider(result) || !hasProviderFilter(result)
    ? 'How your provider compares'
    : `How ${filteredProviders(result.query)} compare`
}

// ---- Plans ----

export interface SpreadLine {
  groupKey: string
  group: string
  providers: number
  summary: PriceSummary
  /** High end over low end, leaving out the top and bottom tenth. */
  ratio: number
  /** How much less the lower-priced quarter get than typical, per case (or per unit), from the figures as shown (gapOf). */
  gap: number | null
  perUnit: boolean
}

/** Groups with 10+ providers: the only ones where "how much prices vary" can be said. */
export function spreadLines(result: RateSearchResult): SpreadLine[] {
  return result.groups.flatMap((group): SpreadLine[] => {
    const { p10, p90 } = group.stats
    if (group.providerCount < MIN_PROVIDERS_FOR_RANGE || p10 == null || p90 == null || p10 <= 0) return []
    return [
      {
        groupKey: group.key,
        group: groupName(group),
        providers: group.providerCount,
        summary: priceSummary(group),
        ratio: p90 / p10,
        gap: gapOf(priceSummary(group)),
        perUnit: group.perUnit,
      },
    ]
  })
}

/** Where prices vary most, first. A group where most providers get about the same price has nothing to show. */
export function widestSpread(lines: SpreadLine[], count = 3): SpreadLine[] {
  return lines
    .filter((line) => line.ratio - 1 >= ABOUT_THE_SAME)
    .sort((a, b) => b.ratio - a.ratio || b.providers - a.providers)
    .slice(0, count)
}

/** "Even leaving out the extremes, the high end is about 1.8 times the low end. $124 less per case at a lower-priced provider." */
export function spreadText(line: SpreadLine): string {
  return [spreadLine(line.summary), perCaseLine(line.gap, line.perUnit)].filter(Boolean).join(' ')
}

// ---- Employers ----

export interface CostLine {
  groupKey: string
  group: string
  providers: number
  /** The typical price (5+ providers). */
  typical: number
  /** How much less the lower-priced quarter get than typical (10+ providers), from the figures as shown (gapOf): per case, or per unit when perUnit. */
  gap: number | null
  perUnit: boolean
  /** The gap times the reader's cases a year. Never for per-unit prices: a case can use several units. */
  yearly: number | null
}

export function costLines(result: RateSearchResult, cases?: number | null): CostLine[] {
  return result.groups
    .flatMap((group): CostLine[] => {
      const typical = typicalPrice(priceSummary(group))
      if (typical == null) return []
      const gap = gapOf(priceSummary(group))
      const yearly = !group.perUnit && gap != null && cases ? gap * cases : null
      return [{ groupKey: group.key, group: groupName(group), providers: group.providerCount, typical, gap, perUnit: group.perUnit, yearly }]
    })
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

/** "About $149,000 less a year for your 1,200 cases at lower-priced providers." */
export function yearlyText(line: CostLine, cases: number): string {
  return yearlyLine(line.gap, cases) ?? ''
}

/**
 * Why there's no yearly figure when the reader entered cases: no card priced per case has 10 or more providers, or on
 * those that do the lower-priced quarter of providers get the typical price, so there's nothing less to count.
 */
export function noYearlyReason(lines: CostLine[]): string {
  const perCase = lines.filter((line) => !line.perUnit)
  const tooFew = perCase.some((line) => line.gap == null)
  const noGap = perCase.some((line) => line.gap === 0)
  if (noGap && tooFew) {
    return "No yearly figure here: cards with fewer than 10 providers can't show one, and on the others the lower-priced quarter of providers get the typical price."
  }
  if (noGap) return 'No yearly figure here: the lower-priced quarter of providers get the typical price, so there is nothing less to count.'
  return 'No yearly figure here: it needs a card with 10 or more providers.'
}

// What a member pays and how often a service happens are said once per page, in the footer.
export const EMPLOYER_CAVEATS: readonly string[] = [
  "These are individual-market plans (Ambetter, Essential Plan), not employer plans; an employer's own rates can differ.",
  "Facility and doctor's fees are separate bills for the same visit.",
]

// ---- Innovators ----

/** The clean-up behind the numbers, in plain words; counts of zero are left out. */
export function qualityLines(quality: SearchQuality): string[] {
  const lines = [`${wholeNumber(quality.publishedRows)} published rows became ${plural(quality.uniqueRates, 'unique rate')}.`]
  if (quality.duplicatesCollapsed) lines.push(`${plural(quality.duplicatesCollapsed, 'exact duplicate')} collapsed.`)
  if (quality.maskedTaxIds) lines.push(`${plural(quality.maskedTaxIds, 'tax ID')} masked: prefixes the IRS never issues.`)
  if (quality.wholeTaxIdContracts) lines.push(`${plural(quality.wholeTaxIdContracts, 'whole-tax-ID contract')}: no individual NPIs listed.`)
  return lines
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
  /** Setting, fee and modifiers: "Doctor's office · Doctor's fee · Drug amount discarded (modifier JW)". */
  site: string
  providers: number
  /** "typical" from 5 providers; under 5, the prices themselves. */
  kind: 'typical' | 'list'
  price: number | null
  rates: number[]
}

/**
 * Per-unit prices by site of care, ordered by setting then fee so the same drug reads side by side.
 * Only groups priced per unit and billed under the code itself: revenue-code facility charges aren't unit prices.
 */
export function unitPriceLines(result: RateSearchResult): UnitPriceLine[] {
  return result.groups
    .filter((group) => group.perUnit && !filedUnderRevenueCode(group))
    .flatMap((group): UnitPriceLine[] => {
      const modifiers = group.modifiers.length ? ` · ${modifierText(group.modifiers)}` : ''
      const base = {
        groupKey: group.key,
        product: group.productLabel,
        site: `${group.settingLabel} · ${feeLabel(group.feeType)}${modifiers}`,
        providers: group.providerCount,
      }
      const typical = typicalPrice(priceSummary(group))
      if (typical != null) return [{ ...base, kind: 'typical', price: typical, rates: [] }]
      const rates = [...group.listedRates].sort((a, b) => a - b)
      return rates.length ? [{ ...base, kind: 'list', price: null, rates }] : []
    })
    .sort((a, b) => a.site.localeCompare(b.site) || a.product.localeCompare(b.product))
}

/** "Typical $0.11", or under 5 providers the prices themselves: "$0.21, $0.25". */
export function unitPriceText(line: UnitPriceLine): string {
  return line.kind === 'typical' && line.price != null ? `Typical ${dollars(line.price)}` : line.rates.map((rate) => money(rate)).join(', ')
}

export const DRUG_EXAMPLES: readonly StartExample[] = START_HERE.find((card) => card.key === 'life-sciences')!.examples
