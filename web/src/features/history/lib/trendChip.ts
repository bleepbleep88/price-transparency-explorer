import type { GroupTrend, TrendPoint } from '../api/types'
import { sameAsShown, upDown, type Shown } from './changesText'
import { comparablePoints, monthOf } from './trend'

// The history chip on a price tile (decision 90): a few words on how the typical price moved up to the month on
// screen. "Rates steady since Jul", "Up $12 since Aug", "Down $9 since Aug". A move that only reflects which providers are
// listed is never called a price change (H1 of the #11 review): when every provider listed in both files kept its rate,
// the chip says "Rates steady" even if the typical price moved.

/** A group's history across the loaded files, as GET /api/v1/rates/trend returns it. */
export type TrendGroup = GroupTrend

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

interface Reading {
  date: string | null
  value: number
}

/**
 * The chip for readings up to the file on screen, with how each step moved (kinds[i]: readings[i] to readings[i + 1]):
 * "<steady> since Jul" with no price move, else the last run of price moves the same way ("Up $20 since Jul" for $100,
 * $110, $120), with no amount when a move that only reflects who's listed came after it.
 */
function chipFor(readings: readonly Reading[], kinds: readonly StepKind[], shown: Shown, steady: string): string {
  const last = kinds.lastIndexOf('priced')
  if (last === -1) return `${steady} since ${monthOf(readings[0]!.date)}`
  const direction = Math.sign(readings[last + 1]!.value - readings[last]!.value)
  let start = last
  while (start > 0 && kinds[start - 1] === 'priced' && Math.sign(readings[start]!.value - readings[start - 1]!.value) === direction) start--
  const since = monthOf(readings[start]!.date)
  if (kinds.slice(last + 1).includes('listed')) return `Rates ${direction > 0 ? 'up' : 'down'} since ${since}`
  const amount = upDown(readings[start]!.value, readings[readings.length - 1]!.value, shown)
  return amount ? `${amount} since ${since}` : `${steady} since ${since}`
}

/** The complete points with a value up to the file on screen (the newest when none is picked); none when it has none. */
function upTo(points: readonly TrendPoint[], selectedId: number | null): TrendPoint[] {
  const end = selectedId == null ? points.length - 1 : points.findIndex((point) => point.snapshotId === selectedId)
  return end < 1 ? [] : points.slice(0, end + 1)
}

/**
 * A few words for a price tile, up to the file on screen (the newest when none is picked); null when there's no history
 * to tell: fewer than two complete files with a typical price, or the file on screen has none (it's incomplete, too
 * small for a typical price, or the oldest file).
 */
export function trendChipText(trend: TrendGroup | null | undefined, selectedSnapshotId: number | null): string | null {
  if (!trend) return null
  const shown = upTo(comparablePoints(trend).filter((point) => point.median != null), selectedSnapshotId)
  if (shown.length < 2) return null
  const kinds = shown.slice(1).map((after, index) => stepKind(shown[index]!, after))
  return chipFor(shown.map((point) => ({ date: point.indexDate, value: point.median! })), kinds, 'typical', 'Rates steady')
}

/**
 * The searched provider's own rate, for the providers lens: "Steady since Jul", "Up $17.41 since Jul" (to the cent, as
 * its rate shows). Its own rate moving is a price change, so there's no who's-listed rule here.
 */
export function providerChipText(trend: TrendGroup | null | undefined, selectedSnapshotId: number | null): string | null {
  if (!trend) return null
  const shown = upTo(comparablePoints(trend).filter((point) => point.provider), selectedSnapshotId)
  if (shown.length < 2) return null
  const readings = shown.map((point) => ({ date: point.indexDate, value: point.provider!.rate }))
  const kinds = readings.slice(1).map((after, index): StepKind => (sameAsShown(readings[index]!.value, after.value, 'rate') ? 'same' : 'priced'))
  return chipFor(readings, kinds, 'rate', 'Steady')
}
