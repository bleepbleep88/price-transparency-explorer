import type { GroupTrend, TrendPoint, TrendStep } from '../api/types'
import { sameAsShown, upDown } from './changesText'
import { comparablePoints, monthOf } from './trend'

// The history chip on a price tile (decision 90): a few words on how rates moved up to the month on screen.
// "Rates steady since Jul", "Up $12 since Aug", "Rates up since Aug". Two rules keep it honest:
// - A move that only reflects which providers are listed is never called a price change (H1 of the #11 review): when
//   every provider listed in both files kept its rates, the chip says "Rates steady" even if the typical price moved.
// - An amount is given only when the same providers are listed all along (nobody joined or left, and nobody's clinicians
//   moved between its rates, in the steps the chip covers). Otherwise the typical price's move is partly about who's
//   listed, so the chip says which way rates went, with no amount: "Rates up since Aug".

/** A group's history across the loaded files, as GET /api/v1/rates/trend returns it. */
export type TrendGroup = GroupTrend

interface Step {
  /**
   * same: the typical price looks the same in both files. listed: it moved, but every provider listed in both kept its
   * rates. priced: it moved and rates changed, or the API can't say (no step between the two files).
   */
  kind: 'same' | 'listed' | 'priced'
  /** Who stayed, moved, joined or left; null when the API gives no step between these two files. */
  providers: TrendStep | null
}

function stepBetween(before: TrendPoint, after: TrendPoint): Step {
  const providers = after.sincePrevious?.sinceSnapshotId === before.snapshotId ? after.sincePrevious : null
  if (sameAsShown(before.median!, after.median!, 'typical')) return { kind: 'same', providers }
  return { kind: providers && providers.raised === 0 && providers.lowered === 0 ? 'listed' : 'priced', providers }
}

/** The same providers all along: nobody joined or left, and nobody's clinicians moved between its rates. */
function sameProviders(steps: readonly Step[]): boolean {
  return steps.every((step) => step.providers && !step.providers.joined && !step.providers.left && !step.providers.reweighted)
}

/** Which way rates went in these steps: "up", "down", "mixed"; the typical price's way when the API can't say. */
function rateWay(steps: readonly Step[], typicalWay: number): string {
  const known = steps.every((step) => step.kind !== 'priced' || step.providers)
  const raised = steps.reduce((total, step) => total + (step.providers?.raised ?? 0), 0)
  const lowered = steps.reduce((total, step) => total + (step.providers?.lowered ?? 0), 0)
  if (!known || (!raised && !lowered)) return typicalWay > 0 ? 'up' : 'down'
  if (raised && lowered) return 'mixed'
  return raised ? 'up' : 'down'
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
  // steps[i] is the step from shown[i] to shown[i + 1].
  const steps = shown.slice(1).map((after, index) => stepBetween(shown[index]!, after))

  const last = steps.map((step) => step.kind).lastIndexOf('priced')
  if (last === -1) return `Rates steady since ${monthOf(shown[0]!.indexDate)}`

  // The run of price moves the same way that ends with the last one: "Up $20 since Jul" for $100, $110, $120.
  const way = (index: number) => Math.sign(shown[index + 1]!.median! - shown[index]!.median!)
  let start = last
  while (start > 0 && steps[start - 1]!.kind === 'priced' && way(start - 1) === way(last)) start--
  const since = monthOf(shown[start]!.indexDate)
  const counted = steps.slice(start)

  const amount = sameProviders(counted) ? upDown(shown[start]!.median!, shown[end]!.median!, 'typical') : null
  return amount ? `${amount} since ${since}` : `Rates ${rateWay(counted, way(last))} since ${since}`
}
