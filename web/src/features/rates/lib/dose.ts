import { plural } from '@/lib/format'
import { dollars, MIN_PROVIDERS_FOR_RANGE, shownDollars, typicalPrice } from '@/lib/plainNumbers'
import type { DoseUnit, RateGroup, RateSearchResult } from '../api/types'
import { filedUnderRevenueCode, groupName, priceSummary } from './lenses'
import { DOSE_DECIMALS } from './searchParams'

// The dose calculator's math, in plain words. Drugs are billed in whole HCPCS units of the amount the code's name states
// (J3301 is 10 mg of triamcinolone), rounded up: a 45 mg dose is 5 units, and any dose at all is at least 1. The cost is
// units times a price per unit.

/** More HCPCS units than this for one dose is a typing slip, not a dose. */
export const MAX_DOSE_UNITS = 100_000
/** Units for a dose: the exact share of the code's unit, and the whole units billed (rounded up, at least 1). */
export function doseUnits(dose: number, unit: DoseUnit): { exact: number; units: number; roundedUp: boolean } {
  // Rounded to 6 places first, so 0.3 / 0.1 is 3 units, not 4.
  const exact = Math.round((dose / unit.amount) * 1e6) / 1e6
  const units = Math.max(1, Math.ceil(exact))
  return { exact, units, roundedUp: units !== exact }
}

function amountText(value: number): string {
  return value.toLocaleString('en-US', { maximumFractionDigits: DOSE_DECIMALS })
}

/** "100 units", "2.5 mg", "1 ml": a dose in the code's unit. */
export function doseText(dose: number, unit: DoseUnit): string {
  return unit.unit === 'unit' ? `${amountText(dose)} ${dose === 1 ? 'unit' : 'units'}` : `${amountText(dose)} ${unit.unit}`
}

/** "units", "mg", "ml": what the dose box takes. */
export function doseUnitWord(unit: DoseUnit): string {
  return unit.unit === 'unit' ? 'units' : unit.unit
}

export interface DoseMath {
  units: number
  /** The dose's cost at the price per unit (the typical price), from the figures as shown; null under a cent a unit. */
  cost: number | null
  lines: string[]
}

/**
 * The calculator's sentences for a dose, a code's unit and a typical price per unit, plus "most providers" (10+):
 * "45 mg divided by 10 mg is 4.5. Units are billed whole, so that's 5 units."
 * "At the typical price of $0.95 a unit, 5 units come to $4.75."
 * "Most providers get between $0.07 and $0.12 a unit: $0.35 to $0.60 for this dose."
 * Null for a dose that isn't a positive number, or one over 100,000 units.
 */
export function doseMath(dose: number, unit: DoseUnit, typical: number, most?: { low: number; high: number } | null): DoseMath | null {
  if (!Number.isFinite(dose) || dose <= 0) return null
  const { exact, units, roundedUp } = doseUnits(dose, unit)
  if (units > MAX_DOSE_UNITS) return null
  const billed = plural(units, 'unit')
  const lines: string[] = []

  if (unit.amount === 1 && unit.unit === 'unit') {
    // One unit is one unit: only rounding needs saying.
    lines.push(roundedUp ? `${doseText(dose, unit)}: units are billed whole, so that's ${billed}.` : `That's ${billed}.`)
  } else if (exact < 1) {
    lines.push(`${doseText(dose, unit)} is less than one unit of ${doseText(unit.amount, unit)}. Units are billed whole, so that's ${billed}.`)
  } else {
    const share = `${doseText(dose, unit)} divided by ${doseText(unit.amount, unit)} is ${amountText(exact)}`
    lines.push(roundedUp ? `${share}. Units are billed whole, so that's ${billed}.` : `${share} ${units === 1 ? 'unit' : 'units'}.`)
  }

  // Typical prices come to the cent: $0 means under half a cent a unit, too little to total honestly.
  const price = shownDollars(typical)
  if (price <= 0) {
    lines.push('The typical price is under a cent a unit, too small to total for this dose.')
    return { units, cost: null, lines }
  }
  const cost = Math.round(units * price * 100) / 100
  lines.push(`At the typical price of ${dollars(typical)} a unit, ${billed} come${units === 1 ? 's' : ''} to ${dollars(cost)}.`)

  if (most && shownDollars(most.low) > 0) {
    const [low, high] = [shownDollars(most.low), shownDollars(most.high)]
    const [lowCost, highCost] = [dollars(Math.round(units * low * 100) / 100), dollars(Math.round(units * high * 100) / 100)]
    lines.push(
      dollars(most.low) === dollars(most.high)
        ? `Most providers get ${dollars(most.low)} a unit: ${lowCost} for this dose.`
        : `Most providers get between ${dollars(most.low)} and ${dollars(most.high)} a unit: ${lowCost} to ${highCost} for this dose.`,
    )
  }

  return { units, cost, lines }
}

/** A card's price per unit the calculator can use: its typical price (5+ providers) and, from 10, "most providers". */
export interface DosePriceOption {
  groupKey: string
  label: string
  providers: number
  typical: number
  most: { low: number; high: number } | null
}

/** The cards a dose can be priced from, or, when there are none, why. */
export interface DosePricing {
  options: DosePriceOption[]
  /** Why no card can price a dose; null when one can, or when the drug has no per-unit price at all. */
  reason: string | null
}

/** Discarded drug (JW) or none discarded (JZ): a separate line on the bill, not the price of the dose given. */
function discardLine(group: RateGroup): boolean {
  return group.modifiers.some((modifier) => modifier.code === 'JW' || modifier.code === 'JZ')
}

/**
 * The cards priced per unit under the drug's own code, in dollars, with a typical price, for drug that's given: not
 * revenue-code facility charges (one amount for the case), not a percentage of billed charges, not discarded-drug (JW)
 * or none-discarded (JZ) lines. Cards without modifiers first, then the most providers.
 */
export function dosePricing(result: RateSearchResult): DosePricing {
  const perUnit = result.groups.filter((group) => group.perUnit && !filedUnderRevenueCode(group) && group.negotiatedType !== 'percentage')
  const priced = perUnit.filter((group) => typicalPrice(priceSummary(group)) != null)
  const options = priced
    .filter((group) => !discardLine(group))
    .map((group) => {
      const { p10, p90 } = group.stats
      const most = group.providerCount >= MIN_PROVIDERS_FOR_RANGE && p10 != null && p90 != null ? { low: p10, high: p90 } : null
      return {
        option: { groupKey: group.key, label: groupName(group), providers: group.providerCount, typical: typicalPrice(priceSummary(group))!, most },
        modifiers: group.modifiers.length,
      }
    })
    .sort((a, b) => Number(a.modifiers > 0) - Number(b.modifiers > 0) || b.option.providers - a.option.providers)
    .map(({ option }) => option)

  if (options.length || !perUnit.length) return { options, reason: null }
  if (priced.some((group) => group.modifiers.some((modifier) => modifier.code === 'JW'))) {
    return { options, reason: "This plan lists a price with 5 or more providers only for discarded drug (modifier JW), so we can't price a dose." }
  }
  if (priced.length) {
    return { options, reason: "This plan lists a price with 5 or more providers only with modifier JZ (no drug discarded), so we can't price a dose." }
  }
  return { options, reason: 'Too few providers to price a dose: each card has fewer than 5.' }
}
