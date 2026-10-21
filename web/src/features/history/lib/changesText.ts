import { money, shortDate, wholeDollars, wholeNumber } from '@/lib/format'
import { aboutTimes, capitalize, dollars, shownDollars, TIMES_FROM } from '@/lib/plainNumbers'
import type { ChangeCounts, ChangesResult, RateChange } from '../api/types'

// Plain words for the changes feed (decision 89): a rate went up, went down, is new or is gone, in dollars and percent.
// A change is what the plan's file says, never what anyone paid.

function joinAnd(parts: string[]): string {
  if (parts.length <= 1) return parts.join('')
  return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`
}

/** "10%", "under 1%", or "about 83 times as much" once the new rate is twice the old or more. */
function share(from: number, to: number): string | null {
  if (from <= 0) return null
  if (to / from >= TIMES_FROM) return `${aboutTimes(to / from)} as much`
  const percent = (Math.abs(to - from) / from) * 100
  return percent < 1 ? 'under 1%' : `${Math.round(percent)}%`
}

/** How an amount is shown: a provider's rate to the cent, as tables and chips show it; a typical price as cards do. */
export type Shown = 'rate' | 'typical'

const SHOWN: Record<Shown, { value: (amount: number) => number; text: (amount: number) => string }> = {
  rate: { value: (amount) => Math.round(amount * 100) / 100, text: (amount) => money(amount) },
  typical: { value: shownDollars, text: dollars },
}

/** A difference between shown figures: "$60", "$17.41". */
function differenceText(amount: number): string {
  const value = Math.abs(amount)
  return Math.round(value * 100) % 100 === 0 ? wholeDollars(value) : money(value)
}

/**
 * Both amounts as shown and the change between them, worked out from the shown figures so they add up beside the card:
 * rates to the cent ("$100.36" to "$117.77": "up $17.41 (17%)"), typical prices as cards show them ("$113" to "$114":
 * "up $1 (under 1%)").
 */
export function changeAmounts(from: number, to: number, shown: Shown = 'rate'): { from: string; to: string; change: string } {
  const { value, text } = SHOWN[shown]
  const [before, after] = [value(from), value(to)]
  const difference = Math.round((after - before) * 100) / 100
  const portion = share(before, after)
  return {
    from: text(from),
    to: text(to),
    change: difference === 0 ? 'no change' : `${difference > 0 ? 'up' : 'down'} ${differenceText(difference)}${portion ? ` (${portion})` : ''}`,
  }
}

/** "Up $12", "Down $4.50": the difference between two amounts as shown, without the share; null when they look the same. */
export function upDown(from: number, to: number, shown: Shown = 'rate'): string | null {
  const { value } = SHOWN[shown]
  const difference = Math.round((value(to) - value(from)) * 100) / 100
  return difference === 0 ? null : `${difference > 0 ? 'Up' : 'Down'} ${differenceText(difference)}`
}

/** Whether two amounts look the same as shown. */
export function sameAsShown(a: number, b: number, shown: Shown = 'rate'): boolean {
  return SHOWN[shown].value(a) === SHOWN[shown].value(b)
}

/** The change column: "Up $60 (10%)", "Down $50 (10%)", "Up $50" from $0, "New" or "Gone". */
export function changeText(row: RateChange): string {
  if (row.kind === 'new') return 'New'
  if (row.kind === 'dropped') return 'Gone'
  return capitalize(changeAmounts(row.from ?? 0, row.to ?? 0).change)
}

/**
 * Why there are no changes to show yet, naming the files: "Changes show once loading finishes: the numbers for Aug 31,
 * 2026 are still being saved." (or "are being updated" for numbers saved before the files or the app changed).
 */
export function waitingText(result: Pick<ChangesResult, 'from' | 'to'>): string {
  const sides = [result.from, result.to]
  const unbuilt = sides.filter((side) => !side.statisticsBuiltAt).map((side) => shortDate(side.indexDate))
  if (unbuilt.length) return `Changes show once loading finishes: the numbers for ${unbuilt.join(' and ')} are still being saved.`
  const outOfDate = sides.filter((side) => !side.statisticsCurrent).map((side) => shortDate(side.indexDate))
  return `Changes show once loading finishes: the numbers for ${outOfDate.join(' and ')} are being updated.`
}

/** The main view's waiting line; Details names the files (waitingText). */
export const WAITING_SHORT = 'Changes show once loading finishes.'

/** A row's prices in a few characters (decision 90): "$42 → $193", "New · $750", "Gone · $450". */
export function priceMove(row: Pick<RateChange, 'kind' | 'from' | 'to'>): string {
  if (row.kind === 'new') return `New · ${dollars(row.to ?? 0)}`
  if (row.kind === 'dropped') return `Gone · ${dollars(row.from ?? 0)}`
  const [from, to] = [row.from ?? 0, row.to ?? 0]
  // Whole dollars from $100 would hide a move of cents ("$100 → $100"): then both to the cent.
  return dollars(from) === dollars(to) ? `${money(from)} → ${money(to)}` : `${dollars(from)} → ${dollars(to)}`
}

/** "5,855 new and 5,853 gone are the same provider and service, relabeled."; null when none are. */
export function listedElsewhereSummary(counts: ChangeCounts): string | null {
  const parts: string[] = []
  if (counts.newListedElsewhere) parts.push(`${wholeNumber(counts.newListedElsewhere)} new`)
  if (counts.droppedListedElsewhere) parts.push(`${wholeNumber(counts.droppedListedElsewhere)} gone`)
  if (!parts.length) return null
  return `${joinAnd(parts)} ${counts.newListedElsewhere + counts.droppedListedElsewhere === 1 ? 'is' : 'are'} the same provider and service, relabeled.`
}

/** How many relabeled rates the switch adds to the list, for the kind shown: new, gone, or both when no kind is picked. */
export function relabeledCount(counts: ChangeCounts | null | undefined, kind: string | null | undefined): number {
  if (!counts) return 0
  if (kind === 'new') return counts.newListedElsewhere
  if (kind === 'dropped') return counts.droppedListedElsewhere
  return kind ? 0 : counts.newListedElsewhere + counts.droppedListedElsewhere
}
