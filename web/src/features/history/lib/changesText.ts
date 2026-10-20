import { money, plural, shortDate, wholeDollars, wholeNumber } from '@/lib/format'
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

/** "1,975 rates went up, 1,957 went down, 14,632 are new and 16,776 are gone", or null when nothing differs. */
function countsText(counts: ChangeCounts): string | null {
  const parts: string[] = []
  const subject = (count: number) => (parts.length ? wholeNumber(count) : plural(count, 'rate'))
  if (counts.increased) parts.push(`${subject(counts.increased)} went up`)
  if (counts.decreased) parts.push(`${subject(counts.decreased)} went down`)
  if (counts.new) parts.push(`${subject(counts.new)} ${counts.new === 1 ? 'is' : 'are'} new`)
  if (counts.dropped) parts.push(`${subject(counts.dropped)} ${counts.dropped === 1 ? 'is' : 'are'} gone`)
  return parts.length ? joinAnd(parts) : null
}

/** The plans lens: "Since the Aug 31 file, for 99213: 56 rates went up, 57 went down, 683 are new and 618 are gone." */
export function changesSinceText(counts: ChangeCounts, since: string, code: string): string {
  const text = countsText(counts)
  return text ? `Since the ${since} file, for ${code}: ${text}.` : `No rate for ${code} changed since the ${since} file.`
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

/** The changes page's summary of everything the filters cover. */
export function countsSummary(counts: ChangeCounts, from: string, to: string): string {
  const text = countsText(counts)
  const same = counts.unchanged ? ` ${plural(counts.unchanged, 'rate')} stayed the same.` : ''
  return text ? `Between ${from} and ${to}: ${text}.${same}` : `Nothing changed between ${from} and ${to}.${same}`
}

/** How many new and gone rates are only listed differently; null when none are. */
export function listedElsewhereSummary(counts: ChangeCounts): string | null {
  const parts: string[] = []
  if (counts.newListedElsewhere) parts.push(`${wholeNumber(counts.newListedElsewhere)} of the new`)
  if (counts.droppedListedElsewhere) parts.push(`${wholeNumber(counts.droppedListedElsewhere)} of the gone`)
  if (!parts.length) return null
  return `${joinAnd(parts)} rates are the same provider and service, listed with other modifiers or in another setting.`
}
