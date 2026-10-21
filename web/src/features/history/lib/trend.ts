import { money } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import type { GroupTrend, RateTrendResult, TrendPoint, TrendStep } from '../api/types'
import { sameAsShown } from './changesText'

// Short readings of a group's history across the plan's monthly files (decisions 89 and 90): prices by month, and who
// stayed or left for the last move. The tile's chip (trendChip.ts) is the one-line answer; these sit with the chart.
// Every number comes from the trend response, which is each file's own search; nothing is estimated.

const month = new Intl.DateTimeFormat('en-US', { month: 'short', timeZone: 'UTC' })

/** "2026-07-27" → "Jul": the files are monthly. */
export function monthOf(date: string | null): string {
  const parsed = date ? new Date(`${date.slice(0, 10)}T00:00:00Z`) : null
  return parsed && !Number.isNaN(parsed.getTime()) ? month.format(parsed) : '—'
}

/** Points from files that look complete: an incomplete file would read as a drop. */
export function comparablePoints(trend: GroupTrend): TrendPoint[] {
  return trend.points.filter((point) => !point.partial)
}

/** The complete files up to the one on screen (the newest when none is picked), or none when it isn't among them. */
function upToSelected(points: readonly TrendPoint[], selectedId: number | null): TrendPoint[] {
  if (selectedId == null) return [...points]
  const end = points.findIndex((point) => point.snapshotId === selectedId)
  return end < 0 ? [] : points.slice(0, end + 1)
}

/** "Jul $51.45 · Aug $51.45 · Sep $114": the typical price in each complete file that has one; null under two. */
export function typicalByMonth(trend: GroupTrend): string | null {
  const points = comparablePoints(trend).filter((point) => point.median != null)
  return points.length < 2 ? null : points.map((point) => `${monthOf(point.indexDate)} ${dollars(point.median!)}`).join(' · ')
}

/** "Jul $100.36 · Aug $100.36 · Sep $117.77", "Jul none" where it has no rate; null without one to follow. */
export function providerByMonth(trend: GroupTrend): string | null {
  const points = comparablePoints(trend)
  if (!points.some((point) => point.provider)) return null
  return points.map((point) => `${monthOf(point.indexDate)} ${point.provider ? money(point.provider.rate) : 'none'}`).join(' · ')
}

/** Who stayed and who came or went in one step: "37 kept their rate, 87 left, 39 joined". */
export function whoText(step: TrendStep): string {
  const kept = step.stayed - step.raised - step.lowered
  const stayed =
    step.raised === 0 && step.lowered === 0
      ? [step.stayed === 0 ? 'none in both' : step.stayed === 1 ? '1 kept its rate' : `${step.stayed} kept their rate`]
      : [kept ? `${kept} same rate` : '', step.raised ? `${step.raised} up` : '', step.lowered ? `${step.lowered} down` : '']
  return [
    ...stayed,
    step.left ? `${step.left} left` : '',
    step.joined ? `${step.joined} joined` : '',
    step.reweighted ? `${step.reweighted} moved clinicians` : '',
  ]
    .filter(Boolean)
    .join(', ')
}

/**
 * Who stayed and who came or went for the last move of the typical price, up to the file on screen: "Aug–Sep: 37 kept
 * their rate, 87 left, 39 joined". It says what the chip's "Rates steady" means when only who is listed changed (H1).
 * Null when the price never moved, or the API gives no step between those two files.
 */
export function whoLine(trend: GroupTrend, selectedId: number | null): string | null {
  const points = upToSelected(
    comparablePoints(trend).filter((point) => point.median != null),
    selectedId,
  )
  for (let index = points.length - 1; index > 0; index--) {
    const [before, after] = [points[index - 1]!, points[index]!]
    if (sameAsShown(before.median!, after.median!, 'typical')) continue
    const step = after.sincePrevious?.sinceSnapshotId === before.snapshotId ? after.sincePrevious : null
    return step ? `${monthOf(before.indexDate)}–${monthOf(after.indexDate)}: ${whoText(step)}` : null
  }
  return null
}

/** The chart's tooltip line for a file whose typical price moved since the one before; null otherwise. */
export function stepNote(trend: GroupTrend, snapshotId: number): string | null {
  const points = comparablePoints(trend).filter((point) => point.median != null)
  const index = points.findIndex((point) => point.snapshotId === snapshotId)
  if (index < 1) return null
  const [before, after] = [points[index - 1]!, points[index]!]
  if (sameAsShown(before.median!, after.median!, 'typical')) return null
  const step = after.sincePrevious?.sinceSnapshotId === before.snapshotId ? after.sincePrevious : null
  return step ? `Since ${monthOf(before.indexDate)}: ${whoText(step)}` : null
}

/** The provider a card follows, by the name its card shows: the file on screen's, else the newest file's. */
export function followedProvider(trend: GroupTrend, selectedId: number | null): string | null {
  const withProvider = trend.points.filter((point) => point.provider)
  const point = withProvider.find((candidate) => candidate.snapshotId === selectedId) ?? withProvider[withProvider.length - 1]
  return point?.provider?.displayName ?? null
}

/** Says which files a line leaves out because they look incomplete. */
export function partialNote(trend: GroupTrend): string | null {
  const partial = trend.points.filter((point) => point.partial).map((point) => monthOf(point.indexDate))
  if (!partial.length) return null
  return `${partial.join(' and ')} ${partial.length === 1 ? 'file' : 'files'} incomplete, left out`
}

export function trendFor(trend: RateTrendResult | null | undefined, groupKey: string): GroupTrend | null {
  return trend?.groups.find((group) => group.key === groupKey) ?? null
}

/** A trend is worth drawing once a group has rates in at least two snapshots. */
export function hasHistory(trend: GroupTrend | null | undefined): trend is GroupTrend {
  return (trend?.points.length ?? 0) >= 2
}

/** A chart needs a line to draw: a typical price in two complete files, or the provider's rate in two. */
export function drawableSeries(trend: GroupTrend, withProvider: boolean): boolean {
  const points = comparablePoints(trend)
  return points.filter((point) => point.median != null).length >= 2 || (withProvider && points.filter((point) => point.provider).length >= 2)
}

export interface TrendSeries {
  labels: string[]
  /** The 10th percentile, the base of the "most providers" band (10+ providers only). */
  low: (number | null)[]
  /** 90th minus 10th percentile, stacked on low: the band most providers' prices fall in. */
  band: (number | null)[]
  typical: (number | null)[]
  provider: (number | null)[]
  /** Where the snapshot on screen sits, or -1. */
  selected: number
}

/** The chart's series: one entry per point, with gaps for incomplete files and for groups too small to show them. */
export function trendSeries(trend: GroupTrend, selectedId: number | null, withProvider: boolean): TrendSeries {
  const usable = (point: TrendPoint, value: number | null) => (point.partial ? null : value)
  return {
    labels: trend.points.map((point) => monthOf(point.indexDate)),
    low: trend.points.map((point) => usable(point, point.p10)),
    band: trend.points.map((point) => usable(point, point.p10 != null && point.p90 != null ? point.p90 - point.p10 : null)),
    typical: trend.points.map((point) => usable(point, point.median)),
    provider: trend.points.map((point) => (withProvider ? usable(point, point.provider?.rate ?? null) : null)),
    selected: trend.points.findIndex((point) => point.snapshotId === selectedId),
  }
}
