import type { GroupTrend, TrendPoint } from '../api/types'
import { sameAsShown, upDown } from './changesText'
import { comparablePoints } from './trend'

// The history chip on a price tile (decision 90): a few words on how the typical price moved up to the month on
// screen. "Rates steady since Jul", "Up $12 since Aug", "Down $9 since Aug". A move that only reflects which providers are
// listed is never called a price change (H1 of the #11 review): when every provider listed in both files kept its rate,
// the chip says "Rates steady" even if the typical price moved.

/** A group's history across the loaded files, as GET /api/v1/rates/trend returns it. */
export type TrendGroup = GroupTrend

const month = new Intl.DateTimeFormat('en-US', { month: 'short', timeZone: 'UTC' })

/** "2026-07-27" → "Jul": the files are monthly. */
function monthOf(date: string | null): string {
  const parsed = date ? new Date(`${date.slice(0, 10)}T00:00:00Z`) : null
  return parsed && !Number.isNaN(parsed.getTime()) ? month.format(parsed) : 'the first file'
}

/**
 * same: the typical price looks the same in both files. listed: it moved, but every provider listed in both kept its
 * rates (only who is listed changed). priced: it moved and rates changed, or the API can't say (no step between them).
 */
type StepKind = 'same' | 'listed' | 'priced'

function stepKind(before: TrendPoint, after: TrendPoint): StepKind {
  if (sameAsShown(before.median!, after.median!, 'typical')) return 'same'
  const step = after.sincePrevious?.sinceSnapshotId === before.snapshotId ? after.sincePrevious : null
  return step && step.raised === 0 && step.lowered === 0 ? 'listed' : 'priced'
}

/**
 * A few words for a price tile, up to the file on screen (the newest when none is picked); null when there's no history
 * to tell: fewer than two complete files with a typical price, or the file on screen has none (it's incomplete, too
 * small for a typical price, or the oldest file).
 */
export function trendChipText(trend: TrendGroup | null | undefined, selectedSnapshotId: number | null): string | null {
  if (!trend) return null
  const points = comparablePoints(trend).filter((point) => point.median != null)
  const end = selectedSnapshotId == null ? points.length - 1 : points.findIndex((point) => point.snapshotId === selectedSnapshotId)
  if (end < 1) return null
  const shown = points.slice(0, end + 1)
  const kinds = shown.slice(1).map((after, index) => stepKind(shown[index]!, after))

  // kinds[i] is the step from shown[i] to shown[i + 1].
  const last = kinds.lastIndexOf('priced')
  if (last === -1) return `Rates steady since ${monthOf(shown[0]!.indexDate)}`

  // The run of price moves the same way that ends with the last one: "Up $20 since Jul" for $100, $110, $120.
  const direction = Math.sign(shown[last + 1]!.median! - shown[last]!.median!)
  let start = last
  while (start > 0 && kinds[start - 1] === 'priced' && Math.sign(shown[start]!.median! - shown[start - 1]!.median!) === direction) start--
  const since = monthOf(shown[start]!.indexDate)

  // A later move that only reflects who's listed would be counted in an amount to the file on screen: say the way only.
  if (kinds.slice(last + 1).includes('listed')) return `Rates ${direction > 0 ? 'up' : 'down'} since ${since}`
  const amount = upDown(shown[start]!.median!, shown[end]!.median!, 'typical')
  return amount ? `${amount} since ${since}` : `Rates steady since ${since}`
}
