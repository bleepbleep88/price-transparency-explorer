import { monthDay } from '@/lib/format'
import type { GroupTrend, RateTrendResult, TrendPoint } from '../api/types'
import { changeAmounts, sameAsShown, type Shown } from './changesText'

// Plain readings of a group's trend across the plan's monthly files (decision 89: "typical price", never "median").
// Every number comes from the trend response, which is each file's own search; nothing is estimated.

interface Value {
  date: string | null
  value: number
}

/** Points from files that look complete: an incomplete file would read as a drop. */
export function comparablePoints(trend: GroupTrend): TrendPoint[] {
  return trend.points.filter((point) => !point.partial)
}

function fileList(values: readonly Value[]): string {
  const dates = values.map((entry) => monthDay(entry.date))
  if (dates.length > 3) return `all ${dates.length} files`
  const list = dates.length <= 1 ? dates.join('') : `${dates.slice(0, -1).join(', ')} and ${dates[dates.length - 1]}`
  return `the ${list} ${dates.length === 1 ? 'file' : 'files'}`
}

/**
 * "The typical price went from $573 (Jul 27) to $569 (Sep 28): down $4 (under 1%)." The same amount (as shown) in every
 * file reads "The typical price was $51.45 in the Jul 27, Aug 31 and Sep 28 files."
 */
export function movementText(subject: string, values: readonly Value[], shown: Shown = 'rate'): string | null {
  if (values.length < 2) return null
  const first = values[0]!
  const last = values[values.length - 1]!
  if (values.every((entry) => sameAsShown(entry.value, first.value, shown))) {
    return `${subject} was ${changeAmounts(first.value, first.value, shown).from} in ${fileList(values)}.`
  }
  const amounts = changeAmounts(first.value, last.value, shown)
  return `${subject} went from ${amounts.from} (${monthDay(first.date)}) to ${amounts.to} (${monthDay(last.date)}): ${amounts.change}.`
}

/** The group's typical price over the complete files, or null when fewer than two have one (under 5 providers, none). */
export function typicalTrendText(trend: GroupTrend): string | null {
  const values = comparablePoints(trend)
    .filter((point) => point.median != null)
    .map((point) => ({ date: point.indexDate, value: point.median! }))
  return movementText('The typical price', values, 'typical')
}

/** The searched provider's rate over the complete files, naming the files where it has none. */
export function providerTrendText(trend: GroupTrend, subject = 'Your rate'): string | null {
  const points = comparablePoints(trend)
  const values = points.filter((point) => point.provider).map((point) => ({ date: point.indexDate, value: point.provider!.rate }))
  if (values.length === 0) return null
  const missing = points.filter((point) => !point.provider).map((point) => monthDay(point.indexDate))
  const absent = missing.length ? ` No rate in the ${missing.join(' or ')} file.` : ''
  if (values.length === 1) {
    return `${subject}: ${changeAmounts(values[0]!.value, values[0]!.value).from} (${monthDay(values[0]!.date)}).${absent}`
  }
  return `${movementText(subject, values)}${absent}`
}

/** Says which files a line leaves out because they look incomplete. */
export function partialNote(trend: GroupTrend): string | null {
  const partial = trend.points.filter((point) => point.partial).map((point) => monthDay(point.indexDate))
  if (!partial.length) return null
  return partial.length === 1
    ? `The ${partial[0]} file looks incomplete, so it's left out.`
    : `The ${partial.join(' and ')} files look incomplete, so they're left out.`
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
    labels: trend.points.map((point) => monthDay(point.indexDate)),
    low: trend.points.map((point) => usable(point, point.p10)),
    band: trend.points.map((point) => usable(point, point.p10 != null && point.p90 != null ? point.p90 - point.p10 : null)),
    typical: trend.points.map((point) => usable(point, point.median)),
    provider: trend.points.map((point) => (withProvider ? usable(point, point.provider?.rate ?? null) : null)),
    selected: trend.points.findIndex((point) => point.snapshotId === selectedId),
  }
}
