import { plural, signedMoney, signedPercent, wholeNumber } from '@/lib/format'
import type { ChangeCounts, RateChange } from '../api/types'

// Plain words for the changes feed. A change is what the plan's file says; it is never called a claim or a saving.

function joinAnd(parts: string[]): string {
  if (parts.length <= 1) return parts.join('')
  return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`
}

/** "113 rates changed (56 up, 57 down), 683 new and 618 dropped", or null when nothing differs. */
function countsText(counts: ChangeCounts): string | null {
  const parts: string[] = []
  if (counts.changed) parts.push(`${plural(counts.changed, 'rate')} changed (${wholeNumber(counts.increased)} up, ${wholeNumber(counts.decreased)} down)`)
  if (counts.new) parts.push(`${wholeNumber(counts.new)} new`)
  if (counts.dropped) parts.push(`${wholeNumber(counts.dropped)} dropped`)
  return parts.length ? joinAnd(parts) : null
}

/** The plans lens: "Since the Aug 31 file, for 99213: 113 rates changed (56 up, 57 down), 683 new and 618 dropped." */
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

/** How many new and dropped rates are only listed differently; null when none are. */
export function listedElsewhereSummary(counts: ChangeCounts): string | null {
  const parts: string[] = []
  if (counts.newListedElsewhere) parts.push(`${wholeNumber(counts.newListedElsewhere)} of the new`)
  if (counts.droppedListedElsewhere) parts.push(`${wholeNumber(counts.droppedListedElsewhere)} of the dropped`)
  if (!parts.length) return null
  return `${joinAnd(parts)} are the same provider and code listed under other modifiers or another setting.`
    .replace(/^./, (letter) => letter.toUpperCase())
}

/** The change column: "+$60.00 (+10.0%)", "+$50.00" from $0, or what the row is when it isn't a change. */
export function changeText(row: RateChange): string {
  if (row.kind === 'new') return 'New'
  if (row.kind === 'dropped') return 'Dropped'
  return row.changePct == null ? signedMoney(row.change) : `${signedMoney(row.change)} (${signedPercent(row.changePct)})`
}
