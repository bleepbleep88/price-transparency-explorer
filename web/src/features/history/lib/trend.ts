import { money, monthDay, signedPercent } from '@/lib/format'
import type { GroupTrend, RateTrendResult, TrendPoint } from '../api/types'

// Plain readings of a group's trend across snapshots. Every number comes from the trend response (which is each
// snapshot's own search); nothing is estimated.

interface Value {
  date: string | null
  value: number
}

/** Points from files that look complete: a partial upload would read as a drop. */
export function comparablePoints(trend: GroupTrend): TrendPoint[] {
  return trend.points.filter((point) => !point.partial)
}

function percentChange(from: number, to: number): string | null {
  return from > 0 ? signedPercent(Math.round(((to - from) / from) * 1000) / 10) : null
}

function dateList(values: readonly Value[]): string {
  const dates = values.map((entry) => monthDay(entry.date))
  return dates.length <= 1 ? dates.join('') : `${dates.slice(0, -1).join(', ')} and ${dates[dates.length - 1]}`
}

/**
 * "Median $51.45 (Jul 27) → $51.45 (Aug 31) → $113.59 (Sep 28), +120.8% since Jul 27." With more than three files only
 * the first and last are named. The same amount everywhere reads "Median $51.45 in the Jul 27, Aug 31 and Sep 28 files."
 */
export function movementText(subject: string, values: readonly Value[]): string | null {
  if (values.length < 2) return null
  const first = values[0]!
  const last = values[values.length - 1]!
  if (values.every((entry) => entry.value === first.value)) {
    return values.length <= 3
      ? `${subject} ${money(first.value)} in the ${dateList(values)} files.`
      : `${subject} ${money(first.value)} in all ${values.length} files.`
  }
  const shown = values.length <= 3 ? values : [first, last]
  const steps = shown.map((entry) => `${money(entry.value)} (${monthDay(entry.date)})`).join(' → ')
  const change = percentChange(first.value, last.value)
  return `${subject} ${steps}${change ? `, ${change} since ${monthDay(first.date)}` : ''}.`
}

/** The group's median over the complete files, or null when fewer than two have one (under 5 providers, no median). */
export function medianTrendText(trend: GroupTrend): string | null {
  const values = comparablePoints(trend)
    .filter((point) => point.median != null)
    .map((point) => ({ date: point.indexDate, value: point.median! }))
  return movementText('Median', values)
}

/** The searched provider's rate over the complete files, naming the files where it has none. */
export function providerTrendText(trend: GroupTrend, subject = 'Your rate'): string | null {
  const points = comparablePoints(trend)
  const values = points.filter((point) => point.provider).map((point) => ({ date: point.indexDate, value: point.provider!.rate }))
  if (values.length === 0) return null
  const missing = points.filter((point) => !point.provider).map((point) => monthDay(point.indexDate))
  const absent = missing.length ? ` No rate in the ${missing.join(' or ')} file.` : ''
  if (values.length === 1) return `${subject} ${money(values[0]!.value)} (${monthDay(values[0]!.date)}).${absent}`
  return `${movementText(subject, values)}${absent}`
}

/** Says which files a line leaves out because they look partial. */
export function partialNote(trend: GroupTrend): string | null {
  const partial = trend.points.filter((point) => point.partial).map((point) => monthDay(point.indexDate))
  if (!partial.length) return null
  return `The ${partial.join(' and ')} ${partial.length === 1 ? 'file looks' : 'files look'} partial, so ${partial.length === 1 ? "it's" : "they're"} left out.`
}

export function trendFor(trend: RateTrendResult | null | undefined, groupKey: string): GroupTrend | null {
  return trend?.groups.find((group) => group.key === groupKey) ?? null
}

/** A trend is worth drawing once a group has rates in at least two snapshots. */
export function hasHistory(trend: GroupTrend | null | undefined): trend is GroupTrend {
  return (trend?.points.length ?? 0) >= 2
}

/** A chart needs a line to draw: a median in two complete files, or the provider's rate in two. */
export function drawableSeries(trend: GroupTrend, withProvider: boolean): boolean {
  const points = comparablePoints(trend)
  return points.filter((point) => point.median != null).length >= 2 || (withProvider && points.filter((point) => point.provider).length >= 2)
}

export interface TrendSeries {
  labels: string[]
  p25: (number | null)[]
  /** p75 − p25, stacked on p25 to draw the middle half as a band. */
  band: (number | null)[]
  median: (number | null)[]
  provider: (number | null)[]
  /** Where the snapshot on screen sits, or -1. */
  selected: number
}

/** The chart's series: one entry per point, with gaps for partial files and for groups too small for a median. */
export function trendSeries(trend: GroupTrend, selectedId: number | null, withProvider: boolean): TrendSeries {
  const usable = (point: TrendPoint, value: number | null) => (point.partial ? null : value)
  return {
    labels: trend.points.map((point) => monthDay(point.indexDate)),
    p25: trend.points.map((point) => usable(point, point.p25)),
    band: trend.points.map((point) => usable(point, point.p25 != null && point.p75 != null ? point.p75 - point.p25 : null)),
    median: trend.points.map((point) => usable(point, point.median)),
    provider: trend.points.map((point) => (withProvider ? usable(point, point.provider?.rate ?? null) : null)),
    selected: trend.points.findIndex((point) => point.snapshotId === selectedId),
  }
}
