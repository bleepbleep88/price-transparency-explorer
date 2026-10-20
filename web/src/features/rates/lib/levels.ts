import { plural, wholeNumber } from '@/lib/format'
import { dollars, feeLabel, fewProvidersLine, MIN_PROVIDERS_FOR_RANGE, rangeLine, typicalPrice, type PriceSummary } from '@/lib/plainNumbers'
import type { LevelLadder, LevelLine, LevelStep } from '../api/types'
import { modifierText } from './lenses'

// The level ladder: one visit family's levels side by side for one plan, fee, setting and modifier set, so levels are
// only compared like for like. Plain words only: a typical price per level, and "most providers" from 10 providers.

/** One level on the chosen line; a level with no prices there still gets a step, with 0 providers. */
export interface LadderStep {
  code: string
  level: number
  providers: number
  typical: number | null
  /** "Most providers get between" (10+ providers only): the chart's band. */
  most: { low: number; high: number } | null
  /** "Most providers get between $80 and $159." (10+), "Prices run from $61 to $244." (5 to 9), else null. */
  range: string | null
  /** "Typical price: $114. Most providers get between $80 and $150.", or why there's no price. */
  text: string
}

function summaryOf(step: LevelStep): PriceSummary {
  return { ...step.stats, providers: step.providerCount }
}

/** Lines with a typical price (5+ providers) at two levels or more: the only ones a ladder can compare. */
export function comparableLines(ladder: LevelLadder | null | undefined): LevelLine[] {
  return (ladder?.lines ?? []).filter((line) => line.levels.filter((step) => typicalPrice(summaryOf(step)) != null).length >= 2)
}

/**
 * "Essential Plan · Doctor's fee · Doctor's office · Separate service, same day (modifier 25)", with the rate type when
 * it isn't a plain negotiated rate ("· rate type: fee schedule"), so two lines never read the same.
 */
export function lineLabel(line: LevelLine): string {
  const modifiers = line.modifiers.length ? ` · ${modifierText(line.modifiers)}` : ''
  const rateType = line.negotiatedType === 'negotiated' ? '' : ` · rate type: ${line.negotiatedType}`
  return `${line.productLabel} · ${feeLabel(line.feeType)} · ${line.settingLabel}${modifiers}${rateType}`
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

function typicalLevels(line: LevelLine): number {
  return line.levels.filter((step) => typicalPrice(summaryOf(step)) != null).length
}

/**
 * The lines the search allows and the one to show first: the picked card's when it compares two levels or more, else
 * the fullest ladder (the API lists lines with the most levels priced first) with a typical price at the searched level,
 * else the first. When the picked card's line can't be compared, the note says so and names the line shown.
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
  const priced = typicalLevels(pickedLine)
  const why = priced === 1 ? 'has a typical price at only one level' : 'has too few providers for a typical price at any level'
  return { lines, line: fallback, note: `The card you picked ${why}; showing ${lineLabel(fallback)}.` }
}

/** One step per level of the family, lowest first, in plain words. */
export function ladderSteps(ladder: LevelLadder, line: LevelLine): LadderStep[] {
  return ladder.codes.map(({ code, level }) => {
    const step = line.levels.find((candidate) => candidate.code === code)
    if (!step) return { code, level, providers: 0, typical: null, most: null, range: null, text: 'No prices for this level here.' }
    const summary = summaryOf(step)
    const typical = typicalPrice(summary)
    if (typical == null) {
      return { code, level, providers: step.providerCount, typical: null, most: null, range: null, text: fewProvidersLine(step.providerCount) }
    }
    const most =
      step.providerCount >= MIN_PROVIDERS_FOR_RANGE && step.stats.p10 != null && step.stats.p90 != null
        ? { low: step.stats.p10, high: step.stats.p90 }
        : null
    const range = rangeLine(summary)
    return { code, level, providers: step.providerCount, typical, most, range, text: `Typical price: ${dollars(typical)}. ${range ?? ''}`.trim() }
  })
}

/** "Level 3 (99213)" */
export function levelLabel(step: { level: number; code: string }): string {
  return `Level ${step.level} (${step.code})`
}

/** "Each level's typical price comes from 44 to 151 providers." over the steps with a typical price. */
export function ladderCaption(steps: readonly LadderStep[]): string {
  const counts = steps.filter((step) => step.typical != null).map((step) => step.providers)
  const [low, high] = [Math.min(...counts), Math.max(...counts)]
  return low === high
    ? `Each level's typical price comes from ${plural(low, 'provider')}.`
    : `Each level's typical price comes from ${wholeNumber(low)} to ${wholeNumber(high)} providers.`
}
