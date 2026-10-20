import { plural } from '@/lib/format'
import { dollars, MIN_PROVIDERS_FOR_RANGE, shownDollars, typicalPrice } from '@/lib/plainNumbers'
import type { DoseUnit, RateSearchResult } from '../api/types'
import { filedUnderRevenueCode, groupName, priceSummary } from './lenses'

// The dose calculator's math, in plain words. Drugs are billed in whole HCPCS units of the amount the code's name states
// (J2704 is 10 mg of propofol), rounded up: a 125 mg dose is 13 units. The cost is units times a price per unit.

/** Units for a dose: the exact share of the code's unit, and the whole units billed (rounded up). */
export function doseUnits(dose: number, unit: DoseUnit): { exact: number; units: number; roundedUp: boolean } {
  // Rounded to 6 places first, so 0.3 / 0.1 is 3 units, not 4.
  const exact = Math.round((dose / unit.amount) * 1e6) / 1e6
  const units = Math.ceil(exact)
  return { exact, units, roundedUp: units !== exact }
}

/** A per-unit price as shown: cents as usual, but four places under a cent, so tiny unit prices aren't shown as $0. */
export function unitPriceText(price: number): string {
  return price > 0 && price < 0.01 ? `$${price.toFixed(4)}` : dollars(price)
}

/** The price a line's math uses: the one shown, so the sentence adds up. */
function shownUnitPrice(price: number): number {
  return price > 0 && price < 0.01 ? price : shownDollars(price)
}

function amountText(value: number): string {
  return value.toLocaleString('en-US', { maximumFractionDigits: 4 })
}

/** "100 units", "2.5 mg", "1 ml": a dose in the code's unit. */
export function doseText(dose: number, unit: DoseUnit): string {
  return unit.unit === 'unit' ? `${amountText(dose)} ${dose === 1 ? 'unit' : 'units'}` : `${amountText(dose)} ${unit.unit}`
}

export interface DoseMath {
  units: number
  /** The dose's cost at the price per unit (the typical price), from the figures as shown. */
  cost: number
  lines: string[]
}

/**
 * The calculator's sentences for a dose, a code's unit and a typical price per unit, plus "most providers" (10+):
 * "125 mg divided by 10 mg is 12.5. Units are billed whole, so that's 13 units."
 * "At the typical price of $0.09 a unit, 13 units come to $1.17."
 * "Most providers get between $0.07 and $0.12 a unit: $0.91 to $1.56 for this dose."
 * Null for a dose that isn't a positive number.
 */
export function doseMath(dose: number, unit: DoseUnit, typical: number, most?: { low: number; high: number } | null): DoseMath | null {
  if (!Number.isFinite(dose) || dose <= 0) return null
  const { exact, units, roundedUp } = doseUnits(dose, unit)
  const billed = plural(units, 'unit')
  const lines: string[] = []

  if (unit.amount === 1 && unit.unit === 'unit') {
    // One unit is one unit: only rounding needs saying.
    lines.push(roundedUp ? `${doseText(dose, unit)}: units are billed whole, so that's ${billed}.` : `That's ${billed}.`)
  } else {
    const share = `${doseText(dose, unit)} divided by ${doseText(unit.amount, unit)} is ${amountText(exact)}`
    lines.push(roundedUp ? `${share}. Units are billed whole, so that's ${billed}.` : `${share} ${units === 1 ? 'unit' : 'units'}.`)
  }

  const price = shownUnitPrice(typical)
  const cost = Math.round(units * price * 100) / 100
  lines.push(`At the typical price of ${unitPriceText(typical)} a unit, ${billed} come${units === 1 ? 's' : ''} to ${dollars(cost)}.`)

  if (most) {
    const [low, high] = [shownUnitPrice(most.low), shownUnitPrice(most.high)]
    const [lowCost, highCost] = [dollars(Math.round(units * low * 100) / 100), dollars(Math.round(units * high * 100) / 100)]
    lines.push(
      unitPriceText(most.low) === unitPriceText(most.high)
        ? `Most providers get ${unitPriceText(most.low)} a unit: ${lowCost} for this dose.`
        : `Most providers get between ${unitPriceText(most.low)} and ${unitPriceText(most.high)} a unit: ${lowCost} to ${highCost} for this dose.`,
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

/**
 * The cards priced per unit under the drug's own code (revenue-code facility charges are one amount for the case, not
 * a unit price), with a typical price. Cards without modifiers first, then the most providers.
 */
export function dosePriceOptions(result: RateSearchResult): DosePriceOption[] {
  return result.groups
    .filter((group) => group.perUnit && !filedUnderRevenueCode(group))
    .flatMap((group): (DosePriceOption & { modifiers: number })[] => {
      const summary = priceSummary(group)
      const typical = typicalPrice(summary)
      if (typical == null) return []
      const { p10, p90 } = group.stats
      const most = group.providerCount >= MIN_PROVIDERS_FOR_RANGE && p10 != null && p90 != null ? { low: p10, high: p90 } : null
      return [{ groupKey: group.key, label: groupName(group), providers: group.providerCount, typical, most, modifiers: group.modifiers.length }]
    })
    .sort((a, b) => Number(a.modifiers > 0) - Number(b.modifiers > 0) || b.providers - a.providers)
    .map(({ modifiers: _modifiers, ...option }) => option)
}
