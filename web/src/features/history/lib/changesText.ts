import { money, plural, wholeNumber } from '@/lib/format'
import { aboutTimes, capitalize, dollars, TIMES_FROM } from '@/lib/plainNumbers'
import type { ChangeCounts, RateChange } from '../api/types'

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

/**
 * Both amounts and the change between them, in one precision so they add up: whole dollars when both are ("$600",
 * "$660", "up $60 (10%)"), cents otherwise ("$100.36", "$117.77", "up $17.41 (17%)").
 */
export function changeAmounts(from: number, to: number): { from: string; to: string; change: string } {
  const whole = [from, to].every((value) => Math.round(value * 100) % 100 === 0)
  const format = (value: number) => (whole ? dollars(value) : money(Math.abs(value)))
  const difference = to - from
  const direction = difference > 0 ? 'up' : 'down'
  const portion = share(from, to)
  return {
    from: format(from),
    to: format(to),
    change: difference === 0 ? 'no change' : `${direction} ${format(difference)}${portion ? ` (${portion})` : ''}`,
  }
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
