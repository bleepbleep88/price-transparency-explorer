import { monthDay, wholeNumber } from '@/lib/format'
import type { GroupTrend, RateTrendResult, TrendPoint, TrendStep } from '../api/types'
import { changeAmounts, sameAsShown, type Shown } from './changesText'

// Plain readings of a group's trend across the plan's monthly files (decision 89: "typical price", never "median").
// Every number comes from the trend response, which is each file's own search; nothing is estimated.

interface Value {
  snapshotId: number
  date: string | null
  value: number
}

/** Points from files that look complete: an incomplete file would read as a drop. */
export function comparablePoints(trend: GroupTrend): TrendPoint[] {
  return trend.points.filter((point) => !point.partial)
}

function joinAnd(parts: string[]): string {
  return parts.length <= 1 ? parts.join('') : `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`
}

function fileList(values: readonly Value[]): string {
  if (values.length > 3) return `all ${values.length} files`
  return `the ${joinAnd(values.map((entry) => monthDay(entry.date)))} ${values.length === 1 ? 'file' : 'files'}`
}

/** Every step goes the same way (or stays), and the ends differ: then "since Jul 27" sums it up truthfully. */
function oneWay(values: readonly Value[], shown: Shown): boolean {
  const shownValues = values.map((entry) => changeAmounts(entry.value, entry.value, shown).from)
  if (shownValues[0] === shownValues[shownValues.length - 1]) return false
  const ups = values.slice(1).map((entry, index) => Math.sign(entry.value - values[index]!.value))
  return ups.every((sign) => sign >= 0) || ups.every((sign) => sign <= 0)
}

/**
 * Two files: "The typical price went from $573 (Jul 27) to $569 (Sep 28): down $4 (under 1%)." Three: every file named,
 * so the one on screen is never skipped: "The typical price was $62 (Jul 27), $71 (Aug 31, this file) and $62 (Sep 28)."
 * with ": up $62.55 (...) since Jul 27" only when every step went the same way. The same amount in every file reads
 * "... was $51.45 in the Jul 27, Aug 31 and Sep 28 files." More than three: the ends, and the file on screen.
 */
export function movementText(subject: string, values: readonly Value[], shown: Shown = 'rate', selectedId: number | null = null): string | null {
  if (values.length < 2) return null
  const first = values[0]!
  const last = values[values.length - 1]!
  const text = (value: number) => changeAmounts(value, value, shown).from
  if (values.every((entry) => sameAsShown(entry.value, first.value, shown))) return `${subject} was ${text(first.value)} in ${fileList(values)}.`
  if (values.length === 2) {
    const amounts = changeAmounts(first.value, last.value, shown)
    return `${subject} went from ${amounts.from} (${monthDay(first.date)}) to ${amounts.to} (${monthDay(last.date)}): ${amounts.change}.`
  }
  const mark = (entry: Value) => (entry.snapshotId === selectedId && entry !== last ? ', this file' : '')
  if (values.length === 3) {
    const list = joinAnd(values.map((entry) => `${text(entry.value)} (${monthDay(entry.date)}${mark(entry)})`))
    const total = oneWay(values, shown) ? `: ${changeAmounts(first.value, last.value, shown).change} since ${monthDay(first.date)}` : ''
    return `${subject} was ${list}${total}.`
  }
  const amounts = changeAmounts(first.value, last.value, shown)
  const middle = values.slice(1, -1).find((entry) => entry.snapshotId === selectedId)
  const onScreen = middle ? ` In the ${monthDay(middle.date)} file on screen it was ${text(middle.value)}.` : ''
  return `${subject} went from ${amounts.from} (${monthDay(first.date)}) to ${amounts.to} (${monthDay(last.date)}): ${amounts.change}.${onScreen}`
}

/**
 * Why the typical price moved, from the providers behind it (H1 of the #11 review): when every provider listed in two
 * files in a row kept its rate, the move comes only from which providers are listed, and the line says so instead of
 * reading like a price change. Null when the typical price didn't move.
 */
export function whyText(points: readonly TrendPoint[]): string | null {
  const steps: TrendStep[] = []
  for (let index = 1; index < points.length; index++) {
    const [before, after] = [points[index - 1]!, points[index]!]
    if (before.median == null || after.median == null || sameAsShown(before.median, after.median, 'typical')) continue
    const step = after.sincePrevious
    // Only a step between these two files explains this move (a file between them without a typical price would make it
    // another pair); without one, say nothing rather than half the story.
    if (!step || step.sinceSnapshotId !== before.snapshotId) return null
    steps.push(step)
  }
  if (!steps.length) return null
  const sum = (pick: (step: TrendStep) => number) => steps.reduce((total, step) => total + pick(step), 0)
  const [raised, lowered, left, joined] = [sum((s) => s.raised), sum((s) => s.lowered), sum((s) => s.left), sum((s) => s.joined)]
  const pair = points.length === 2 ? 'both files' : 'two files in a row'
  const churn = joinAnd([left ? `${wholeNumber(left)} left` : '', joined ? `${wholeNumber(joined)} joined` : ''].filter(Boolean))
  if (raised === 0 && lowered === 0) {
    return churn
      ? `Providers listed in ${pair} kept their rates: the typical price moved because different providers are listed (${churn}).`
      : null
  }
  const moved = joinAnd([raised ? `${wholeNumber(raised)} got a higher rate` : '', lowered ? `${wholeNumber(lowered)} a lower one` : ''].filter(Boolean))
  return `Of the providers listed in ${pair}, ${moved}${churn ? `; ${churn}` : ''}.`
}

/** The group's typical price over the complete files, and why it moved; null when fewer than two files have one. */
export function typicalTrendText(trend: GroupTrend, selectedId: number | null = null): string | null {
  const points = comparablePoints(trend).filter((point) => point.median != null)
  const values = points.map((point) => ({ snapshotId: point.snapshotId, date: point.indexDate, value: point.median! }))
  const movement = movementText('The typical price', values, 'typical', selectedId)
  if (!movement) return null
  const why = whyText(points)
  return why ? `${movement} ${why}` : movement
}

/**
 * The searched provider's rate over the complete files, naming the files where it has none. The subject is "Your rate"
 * only where the reader is the provider (the providers lens); a card names the provider ("The rate for PM Pediatrics").
 */
export function providerTrendText(trend: GroupTrend, subject: string, selectedId: number | null = null): string | null {
  const points = comparablePoints(trend)
  const values = points
    .filter((point) => point.provider)
    .map((point) => ({ snapshotId: point.snapshotId, date: point.indexDate, value: point.provider!.rate }))
  if (values.length === 0) return null
  const missing = points.filter((point) => !point.provider).map((point) => monthDay(point.indexDate))
  const absent = missing.length ? ` No rate in the ${missing.join(' or ')} file.` : ''
  if (values.length === 1) return `${subject}: ${changeAmounts(values[0]!.value, values[0]!.value).from} (${monthDay(values[0]!.date)}).${absent}`
  return `${movementText(subject, values, 'rate', selectedId)}${absent}`
}

/** The provider a card follows, by the name its card shows: the file on screen's, else the newest file's. */
export function followedProvider(trend: GroupTrend, selectedId: number | null): string | null {
  const withProvider = trend.points.filter((point) => point.provider)
  const point = withProvider.find((candidate) => candidate.snapshotId === selectedId) ?? withProvider[withProvider.length - 1]
  return point?.provider?.displayName ?? null
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
