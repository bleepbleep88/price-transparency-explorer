import { billLine, billName, placesCount } from '@/lib/plainNames'
import { dollars, MIN_PROVIDERS_FOR_RANGE, typicalPrice, type PriceSummary } from '@/lib/plainNumbers'
import type { LevelLadder, LevelLine, LevelStep } from '../api/types'

// The level ladder: one visit family's levels side by side for one plan, bill and setting, so levels are only
// compared like for like. A typical price per level (5+ places), and "most places" from 10.

/** One level on the chosen line; a level with no prices there still gets a step, with 0 places. */
export interface LadderStep {
  code: string
  level: number
  providers: number
  typical: number | null
  /** "Most places" (10+ only). */
  most: { low: number; high: number } | null
  /** "$80 – $159" (10+ places), else null. */
  mostText: string | null
  /** A few words for the chart's tooltip: "Typical $114 · 105 places", "Only 3 places", "No prices". */
  text: string
}

function summaryOf(step: LevelStep): PriceSummary {
  return { ...step.stats, providers: step.providerCount }
}

/** Lines with a typical price (5+ places) at two levels or more: the only ones a ladder can compare. */
export function comparableLines(ladder: LevelLadder | null | undefined): LevelLine[] {
  return (ladder?.lines ?? []).filter((line) => line.levels.filter((step) => typicalPrice(summaryOf(step)) != null).length >= 2)
}

/** The main view's name for a line: plan and bill, no modifiers: "Essential Plan · Doctor · in the office". */
export function lineTitle(line: LevelLine): string {
  const { name, where } = billName(line)
  return [line.productLabel, name, where].filter(Boolean).join(' · ')
}

/**
 * The picker's name for a line, so no two read the same: plan, bill, modifiers in words, and the rate type when it
 * isn't a plain negotiated rate. "Essential Plan · Doctor · in the office · separate visit, same day".
 */
export function lineLabel(line: LevelLine): string {
  const rateType = line.negotiatedType === 'negotiated' ? '' : ` · ${line.negotiatedType} rate`
  return `${line.productLabel} · ${billLine(line)}${rateType}`
}

/** What the search narrows to: the ladder shows only lines for the same plan, setting and fee type, and the picked card's. */
export interface LadderFilters {
  product?: string
  setting?: string
  feeType?: string
  group?: string
}

export interface LadderChoice {
  /** The comparable lines for the search's plan, setting and fee type: what the picker offers. */
  lines: LevelLine[]
  line: LevelLine | null
  /** Set when the picked card's line can't be compared and another is shown instead. */
  note: string | null
}

/**
 * The lines the search allows and the one to show first: the picked card's when it compares two levels or more, else
 * the fullest ladder (the API lists lines with the most levels priced first) with a typical price at the searched level,
 * else the first. When the picked card's line can't be compared, the closest line stands in (same plan, then fee type,
 * then setting) and the note says so in a few words.
 */
export function chooseLine(ladder: LevelLadder | null | undefined, code: string, filters: LadderFilters = {}): LadderChoice {
  const lines = comparableLines(ladder).filter(
    (line) =>
      (!filters.product || line.productKey === filters.product) &&
      (!filters.setting || line.settingKey === filters.setting) &&
      (!filters.feeType || line.feeType === filters.feeType),
  )
  const pricedAt = (line: LevelLine) => line.levels.some((step) => step.code === code && typicalPrice(summaryOf(step)) != null)
  const fallback = lines.find(pricedAt) ?? lines[0] ?? null

  const pickedLine = filters.group ? (ladder?.lines ?? []).find((line) => line.levels.some((step) => step.groupKey === filters.group)) : undefined
  if (!pickedLine) return { lines, line: fallback, note: null }
  if (lines.includes(pickedLine)) return { lines, line: pickedLine, note: null }
  if (!fallback) return { lines, line: null, note: null }

  const likeness = (line: LevelLine) =>
    (line.productKey === pickedLine.productKey ? 4 : 0) + (line.feeType === pickedLine.feeType ? 2 : 0) + (line.settingKey === pickedLine.settingKey ? 1 : 0)
  // Most alike first; among equals, one priced at the searched level, then the API's order (fullest first).
  const closest = lines.reduce((best, line) => {
    const [a, b] = [likeness(line), likeness(best)]
    return a > b || (a === b && pricedAt(line) && !pricedAt(best)) ? line : best
  }, lines[0]!)
  return { lines, line: closest, note: 'Too few levels for this bill. Showing a similar one.' }
}

/** One step per level of the family, lowest first. */
export function ladderSteps(ladder: LevelLadder, line: LevelLine): LadderStep[] {
  return ladder.codes.map(({ code, level }) => {
    const step = line.levels.find((candidate) => candidate.code === code)
    if (!step) return { code, level, providers: 0, typical: null, most: null, mostText: null, text: 'No prices' }
    const typical = typicalPrice(summaryOf(step))
    if (typical == null) {
      return { code, level, providers: step.providerCount, typical: null, most: null, mostText: null, text: `Only ${placesCount(step.providerCount)}` }
    }
    const { p10, p90 } = step.stats
    const most = step.providerCount >= MIN_PROVIDERS_FOR_RANGE && p10 != null && p90 != null ? { low: p10, high: p90 } : null
    const mostText = most ? (dollars(most.low) === dollars(most.high) ? dollars(most.low) : `${dollars(most.low)} – ${dollars(most.high)}`) : null
    return { code, level, providers: step.providerCount, typical, most, mostText, text: `Typical ${dollars(typical)} · ${placesCount(step.providerCount)}` }
  })
}

/** "Level 3". The code goes in Details. */
export function levelLabel(step: { level: number }): string {
  return `Level ${step.level}`
}
