// The search page's answer (decision 90): plan tabs, one tile per bill, other prices, and the "I'm a…" lines.
// Pure functions of the API result, so the wording is unit-tested.

import { money, plural } from '@/lib/format'
import { billLine, billName, placesCount, serviceTitle } from '@/lib/plainNames'
import { dollars, gapOf, lessPerCase, positionLine, bandFor, typicalPrice, yearlyLine, type PriceSummary } from '@/lib/plainNumbers'
import type { RateGroup, RateSearchResult } from '../api/types'
import { isDrugCode, pinsProvider, priceSummary } from './lenses'
import type { Lens } from './searchParams'

export interface PlanTab {
  key: string
  label: string
}

/** Short plan names for the tabs: "Ambetter from Fidelis Care" reads as "Ambetter". */
export function planName(label: string): string {
  return label.replace(/\s+from\s+.*$/i, '').trim() || label
}

/** The plans in the result, the one with the most providers first. */
export function planTabs(result: RateSearchResult): PlanTab[] {
  const totals = new Map<string, { label: string; providers: number }>()
  for (const group of result.groups) {
    const entry = totals.get(group.productKey) ?? { label: group.productLabel, providers: 0 }
    entry.providers += group.providerCount
    totals.set(group.productKey, entry)
  }
  return [...totals.entries()]
    .sort((a, b) => b[1].providers - a[1].providers || a[1].label.localeCompare(b[1].label))
    .map(([key, entry]) => ({ key, label: planName(entry.label) }))
}

/** The plan tab to show: the one asked for when the result has it, else the first. */
export function currentPlan(result: RateSearchResult, asked?: string | null): string | null {
  const tabs = planTabs(result)
  return tabs.find((tab) => tab.key === asked)?.key ?? tabs[0]?.key ?? null
}

function hasTypical(group: RateGroup): boolean {
  return typicalPrice(priceSummary(group)) != null
}

/** A drug price for the amount thrown away (JW) or a no-waste line (JZ): never the answer for a dose. */
export function forDiscardedDrug(group: RateGroup): boolean {
  return group.modifiers.some((modifier) => modifier.code === 'JW' || modifier.code === 'JZ')
}

function byVideo(group: RateGroup): boolean {
  return group.settingKey.startsWith('telehealth') || group.modifiers.some((modifier) => ['95', 'GT', 'GQ', '93', 'FQ'].includes(modifier.code))
}

/**
 * The order tiles and other prices come in: never a thrown-away-drug price first; a price with a typical before one
 * without; in person before video (an office visit leads with the office, when the office has a typical); then the most
 * providers.
 */
function byStrength(a: RateGroup, b: RateGroup): number {
  return (
    Number(forDiscardedDrug(a)) - Number(forDiscardedDrug(b)) ||
    Number(hasTypical(b)) - Number(hasTypical(a)) ||
    Number(byVideo(a)) - Number(byVideo(b)) ||
    b.providerCount - a.providerCount
  )
}

/**
 * The bills a tile row shows for one plan: the strongest group of each fee type (facility, doctor), plus the picked
 * group when it isn't one of them. Revenue-code-only facility charges come after real procedure prices.
 */
export function tileGroups(result: RateSearchResult, plan: string | null, picked?: string | null): RateGroup[] {
  const groups = result.groups.filter((group) => group.productKey === plan)
  const leads: RateGroup[] = []
  for (const feeType of ['facility', 'professional']) {
    const candidates = groups.filter((group) => group.feeType === feeType).sort(byStrength)
    if (candidates[0]) leads.push(candidates[0])
  }
  const pickedGroup = groups.find((group) => group.key === picked)
  if (pickedGroup && !leads.includes(pickedGroup)) leads.push(pickedGroup)
  return leads.sort(byStrength)
}

/** The group the places list follows: the picked one when it belongs to this plan, else the first tile. */
export function leadGroup(result: RateSearchResult, plan: string | null, picked?: string | null): RateGroup | null {
  const tiles = tileGroups(result, plan, picked)
  return tiles.find((group) => group.key === picked) ?? tiles[0] ?? null
}

/** "Most: $389 – $625" from 10 places, "From $185 to $205" from 5, nothing under 5. */
export function shortRange(summary: PriceSummary): string | null {
  if (summary.providers >= 10 && summary.p10 != null && summary.p90 != null) {
    const [low, high] = [dollars(summary.p10), dollars(summary.p90)]
    return low === high ? `Most: ${low}` : `Most: ${low} – ${high}`
  }
  if (summary.providers >= 5) {
    const [low, high] = [dollars(summary.min), dollars(summary.max)]
    return low === high ? `All: ${low}` : `From ${low} to ${high}`
  }
  return null
}

export interface Tile {
  groupKey: string
  label: string
  where: string | null
  price: number | null
  perUnit: boolean
  line: string | null
  listed: number[]
  count: string | null
  /** The searched provider's price on this bill: "Yours: $247.85 · Lower than most · $263.15 less than typical". */
  yours: string | null
}

export function tileFor(group: RateGroup, result: RateSearchResult): Tile {
  const summary = priceSummary(group)
  const typical = typicalPrice(summary)
  const { name, where: place } = billName(group)
  // Only a thrown-away-drug price to show: the tile says so in plain words.
  const where = forDiscardedDrug(group) ? [place, 'for drug thrown away'].filter(Boolean).join(', ') : place
  const position = pinsProvider(result) ? group.highlights[0] : undefined
  let yours: string | null = null
  if (position) {
    const compared = typical == null ? null : positionLine(bandFor(position.rate, summary), position.rate, typical)
    // To the cent, as the difference after it is worked out: $511 − $247.85 = $263.15.
    yours = [`Yours: ${money(position.rate)}`, compared].filter(Boolean).join(' · ')
  }
  return {
    groupKey: group.key,
    label: name,
    where,
    price: typical,
    perUnit: group.perUnit,
    line: shortRange(summary),
    listed: typical == null ? [...group.listedRates].sort((a, b) => a - b) : [],
    // Under 5 the tile lists the prices, which already says how many.
    count: typical == null ? null : placesCount(group.providerCount),
    yours,
  }
}

export interface OtherPrice {
  groupKey: string
  plan: string
  bill: string
  price: string
  count: string
}

/** Every other price for the service, both plans: one short line each, strongest first. */
export function otherPrices(result: RateSearchResult, shown: string[]): OtherPrice[] {
  return result.groups
    .filter((group) => !shown.includes(group.key))
    .sort(byStrength)
    .map((group) => {
      const typical = typicalPrice(priceSummary(group))
      const unit = group.perUnit ? ' per unit' : ''
      return {
        groupKey: group.key,
        plan: planName(group.productLabel),
        bill: billLine(group),
        price: typical == null ? `Only ${group.providerCount === 1 ? '1 place' : `${group.providerCount} places`}` : `${dollars(typical)}${unit}`,
        // "Only 3 places" already says how many.
        count: typical == null ? '' : placesCount(group.providerCount),
      }
    })
}

/**
 * The page title for a search: a plain name, the start of the plan's long description, the short name spelled out, or
 * for a revenue code its label ("Ambulatory surgery charge").
 */
export function resultTitle(result: RateSearchResult): string {
  const code = result.code
  const searched = code?.code ?? result.query.code
  const revenue = result.groups
    .flatMap((group) => group.billedAs)
    .find((billed) => billed.billingCodeType === 'RC' && billed.billingCode === searched)
  const revenueLabel = revenue?.label.match(/\(([^)]+)\)/)?.[1] ?? null
  return serviceTitle(searched, code?.name, code?.descriptionSource === 'payer_file' || code?.descriptionSource === 'cms_hcpcs' ? code?.description : null, revenueLabel)
}

export interface RoleLine {
  text: string
  /** Employers: the yearly figure for the cases entered. */
  yearly?: string | null
  /** Employers: a case count can give a yearly figure here, so the cases box shows. */
  cases?: boolean
  /** A follow-up the line offers: a box for an NPI or tax ID, a link to the API, a drug to try. */
  action?: 'npi' | 'api' | 'drug' | null
}

/** The bill with the most to gain from a cheaper place: the biggest difference between typical and the cheapest quarter. */
export function biggestGap(groups: RateGroup[]): { group: RateGroup; gap: number } | null {
  let best: { group: RateGroup; gap: number } | null = null
  for (const group of groups) {
    const gap = gapOf(priceSummary(group))
    if (gap != null && gap > 0 && (!best || gap > best.gap)) best = { group, gap }
  }
  return best
}

/**
 * One or two short lines for the reader's role, about the bill the places list follows. An employer's line speaks for
 * the bill with the most to gain (of the tiles shown), named, so a fixed doctor's fee doesn't hide a surgery center's.
 */
export function roleLines(lens: Lens, result: RateSearchResult, group: RateGroup | null, cases?: number | null, tiles: RateGroup[] = []): RoleLine[] {
  const summary = group ? priceSummary(group) : null
  switch (lens) {
    case 'providers': {
      const tile = group ? tileFor(group, result) : null
      if (tile?.yours) return [{ text: tile.yours }]
      // An NPI or tax ID is in the search, but not on this bill.
      if (pinsProvider(result)) return [{ text: 'Not on this bill. Pick another above.' }]
      // The strip shows a box for the NPI or tax ID under this line.
      return [{ text: 'Add your NPI or tax ID to see where you stand.', action: 'npi' }]
    }
    case 'plans': {
      if (!group || !summary) return [{ text: 'No prices to compare here.' }]
      if (summary.providers < 10) return [{ text: 'Too few places to compare. Needs 10.' }]
      const range = shortRange(summary)
      return [{ text: `${range?.replace('Most: ', 'Most places get ')}. Highest: ${dollars(summary.max)}.` }]
    }
    case 'employers': {
      const best = biggestGap(tiles.length ? tiles : group ? [group] : [])
      if (!best) {
        const anyRange = (tiles.length ? tiles : group ? [group] : []).some((tile) => gapOf(priceSummary(tile)) != null)
        return [{ text: anyRange ? 'Cheaper places get the same price here.' : 'Needs 10 places to say what cheaper ones save.' }]
      }
      const per = lessPerCase(best.gap, best.group.perUnit)
      const bill = tiles.length > 1 ? `${billName(best.group).name}: ` : ''
      return [
        {
          text: `${bill}the cheapest quarter of places get at least ${per}.`.replace(/^the/, 'The'),
          yearly: !best.group.perUnit && cases ? yearlyLine(best.gap, cases) : null,
          cases: !best.group.perUnit,
        },
      ]
    }
    case 'innovators': {
      const quality = result.quality
      return [
        { text: `${quality.publishedRows.toLocaleString('en-US')} listed prices → ${plural(quality.uniqueRates, 'unique price')}. Each links to its file.`, action: 'api' },
      ]
    }
    case 'life-sciences':
      if (!isDrugCode(result.code)) return [{ text: 'Not a drug. Try Botox (J0585).', action: 'drug' }]
      return [{ text: group?.perUnit && summary && typicalPrice(summary) != null ? `Per unit: ${dollars(typicalPrice(summary)!)} typical.` : 'Per-unit prices are in the tiles above.' }]
  }
}

/** Hints about how the plan filed a code: shown in Details when the search has prices. */
export const TECHNICAL_HINTS: ReadonlySet<string> = new Set(['code_facility_only', 'revenue_code_procedures'])

/** The role chips, in plain words. */
export const ROLE_OPTIONS: readonly { key: Lens; label: string }[] = [
  { key: 'providers', label: 'Provider' },
  { key: 'plans', label: 'Health plan' },
  { key: 'employers', label: 'Employer' },
  { key: 'innovators', label: 'Researcher' },
  { key: 'life-sciences', label: 'Drug maker' },
]
