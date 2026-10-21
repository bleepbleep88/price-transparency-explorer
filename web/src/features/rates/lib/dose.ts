import { plural } from '@/lib/format'
import { billLine } from '@/lib/plainNames'
import { dollars, MIN_PROVIDERS_FOR_RANGE, shownDollars, typicalPrice } from '@/lib/plainNumbers'
import type { DoseUnit, RateGroup, RateSearchResult } from '../api/types'
import { filedUnderRevenueCode, priceSummary } from './lenses'
import { DOSE_DECIMALS } from './searchParams'

// The dose calculator, in one line: "45 mg = 5 units → $4.75". Drugs are billed in whole HCPCS units of the amount the
// code's name states (J3301 is 10 mg of triamcinolone), rounded up, and any dose at all is at least 1 unit. The cost is
// units times the typical price per unit.

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

/** "1 unit = 10 mg"; nothing for a code whose unit is simply one unit. */
export function unitDefinition(unit: DoseUnit): string | null {
  return unit.unit === 'unit' && unit.amount === 1 ? null : `1 unit = ${unit.text}`
}

export interface DoseLine {
  /** "45 mg = 5 units → $4.75". */
  text: string
  units: number
  /** From the figures as shown; null when the typical price is under a cent a unit. */
  cost: number | null
}

/**
 * The dose's answer in one line, from the typical price per unit as shown: "45 mg = 5 units → $4.75",
 * "100 units → $552", "0.5 mg = 1 unit → $0.95", or "→ under 1¢ a unit" when the price rounds to $0.
 * Null for a dose that isn't a positive number, or one over 100,000 units.
 */
export function doseLine(dose: number, unit: DoseUnit, typical: number): DoseLine | null {
  if (!Number.isFinite(dose) || dose <= 0) return null
  const { units, roundedUp } = doseUnits(dose, unit)
  if (units > MAX_DOSE_UNITS) return null
  // A code whose unit is one unit needs no "= N units" unless the dose was rounded up.
  const sameUnits = unit.unit === 'unit' && unit.amount === 1 && !roundedUp
  const left = sameUnits ? doseText(dose, unit) : `${doseText(dose, unit)} = ${plural(units, 'unit')}`
  const price = shownDollars(typical)
  if (price <= 0) return { text: `${left} → under 1¢ a unit`, units, cost: null }
  const cost = Math.round(units * price * 100) / 100
  return { text: `${left} → ${dollars(cost)}`, units, cost }
}

/** "Most places: $0.35 – $0.60" for the dose (10+ places only); null without a range or under a cent a unit. */
export function doseRange(units: number, most: { low: number; high: number } | null): string | null {
  if (!most || shownDollars(most.low) <= 0) return null
  const [low, high] = [shownDollars(most.low), shownDollars(most.high)].map((price) => dollars(Math.round(units * price * 100) / 100))
  return low === high ? `Most places: ${low}` : `Most places: ${low} – ${high}`
}

/** A card's price per unit the calculator can use: its typical price (5+ providers) and, from 10, "most providers". */
export interface DosePriceOption {
  groupKey: string
  /** In plain words: "Essential Plan · Doctor · in the office · given into a vein". */
  label: string
  providers: number
  typical: number
  most: { low: number; high: number } | null
}

/** The cards a dose can be priced from, or, when there are none, why. */
export interface DosePricing {
  options: DosePriceOption[]
  /** Why no card can price a dose, in one short line; null when one can, or when the drug has no per-unit price at all. */
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
        option: {
          groupKey: group.key,
          label: `${group.productLabel} · ${billLine(group)}`,
          providers: group.providerCount,
          typical: typicalPrice(priceSummary(group))!,
          most,
        },
        modifiers: group.modifiers.length,
      }
    })
    .sort((a, b) => Number(a.modifiers > 0) - Number(b.modifiers > 0) || b.option.providers - a.option.providers)
    .map(({ option }) => option)

  if (options.length || !perUnit.length) return { options, reason: null }
  if (priced.some((group) => group.modifiers.some((modifier) => modifier.code === 'JW'))) {
    return { options, reason: 'Only priced for thrown-away drug. No dose price.' }
  }
  if (priced.length) return { options, reason: 'Only priced with none thrown away. No dose price.' }
  return { options, reason: 'Too few prices to work out a dose.' }
}
