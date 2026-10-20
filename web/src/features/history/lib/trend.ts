import { monthDay, wholeNumber } from '@/lib/format'
import { capitalize } from '@/lib/plainNumbers'
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

function joinAnd(parts: readonly string[]): string {
  const kept = parts.filter(Boolean)
  return kept.length <= 1 ? kept.join('') : `${kept.slice(0, -1).join(', ')} and ${kept[kept.length - 1]}`
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

const text = (value: number, shown: Shown) => changeAmounts(value, value, shown).from

/** "$62 (Jul 27), $71 (Aug 31, this file) and $62 (Sep 28)": every file, the one on screen marked unless it's the last. */
function valueList(values: readonly Value[], shown: Shown, selectedId: number | null): string {
  const last = values[values.length - 1]
  const mark = (entry: Value) => (entry.snapshotId === selectedId && entry !== last ? ', this file' : '')
  return joinAnd(values.map((entry) => `${text(entry.value, shown)} (${monthDay(entry.date)}${mark(entry)})`))
}

/** More than three files name the ends; this names the one on screen when it's in between. */
function onScreenNote(values: readonly Value[], shown: Shown, selectedId: number | null): string {
  const middle = values.slice(1, -1).find((entry) => entry.snapshotId === selectedId)
  return middle ? ` In the ${monthDay(middle.date)} file on screen it was ${text(middle.value, shown)}.` : ''
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
  if (values.every((entry) => sameAsShown(entry.value, first.value, shown))) return `${subject} was ${text(first.value, shown)} in ${fileList(values)}.`
  const amounts = changeAmounts(first.value, last.value, shown)
  if (values.length === 2) return `${subject} went from ${amounts.from} (${monthDay(first.date)}) to ${amounts.to} (${monthDay(last.date)}): ${amounts.change}.`
  if (values.length === 3) {
    const total = oneWay(values, shown) ? `: ${amounts.change} since ${monthDay(first.date)}` : ''
    return `${subject} was ${valueList(values, shown, selectedId)}${total}.`
  }
  return `${subject} went from ${amounts.from} (${monthDay(first.date)}) to ${amounts.to} (${monthDay(last.date)}): ${amounts.change}.${onScreenNote(values, shown, selectedId)}`
}

/** Says where the typical price was, with no amount of change: for moves that come only from who is listed. */
function wherePriceWas(values: readonly Value[], selectedId: number | null): string {
  const first = values[0]!
  const last = values[values.length - 1]!
  if (values.length === 3) return `was ${valueList(values, 'typical', selectedId)}.`
  const move = `moved from ${text(first.value, 'typical')} (${monthDay(first.date)}) to ${text(last.value, 'typical')} (${monthDay(last.date)}).`
  return values.length === 2 ? move : `${move}${onScreenNote(values, 'typical', selectedId)}`
}

interface Move {
  before: TrendPoint
  after: TrendPoint
  /** How the providers moved between these two files; null when the API gave a step from another file. */
  step: TrendStep | null
}

/** The steps between consecutive files where the typical price moved, as the cards show it. */
function typicalMoves(points: readonly TrendPoint[]): Move[] {
  const moves: Move[] = []
  for (let index = 1; index < points.length; index++) {
    const [before, after] = [points[index - 1]!, points[index]!]
    if (before.median == null || after.median == null || sameAsShown(before.median, after.median, 'typical')) continue
    // A file between these two without a typical price makes the API's step another pair.
    const step = after.sincePrevious?.sinceSnapshotId === before.snapshotId ? after.sincePrevious : null
    moves.push({ before, after, step })
  }
  return moves
}

function pairName(move: Move, onlyTwo: boolean): string {
  return onlyTwo ? 'both files' : `the ${monthDay(move.before.indexDate)} and ${monthDay(move.after.indexDate)} files`
}

/** Kept every rate: a provider whose clinicians moved between the same rates still lists the same ones. */
function ratesKept(step: TrendStep): boolean {
  return step.raised === 0 && step.lowered === 0
}

/**
 * The providers listed in both files come first (H1 of the #11 review): "Of the 37 providers listed in both files, all
 * kept the same rate." or "..., 28 kept the same rate, 1 got a higher rate and 1 a lower one."
 */
export function stayedText(step: TrendStep, pair: string): string {
  if (step.stayed === 0) return `No provider is listed in ${pair}.`
  if (step.stayed === 1) {
    return ratesKept(step)
      ? `The one provider listed in ${pair} kept the same rate.`
      : `The one provider listed in ${pair} got a ${step.raised ? 'higher' : 'lower'} rate.`
  }
  const kept = step.stayed - step.raised - step.lowered
  if (ratesKept(step)) return `Of the ${wholeNumber(step.stayed)} providers listed in ${pair}, all kept the same rate.`
  const parts = [
    kept ? `${wholeNumber(kept)} kept the same rate` : '',
    step.raised ? `${wholeNumber(step.raised)} got a higher rate` : '',
    step.lowered ? `${wholeNumber(step.lowered)} ${step.raised ? 'a lower one' : 'got a lower rate'}` : '',
  ]
  return `Of the ${wholeNumber(step.stayed)} providers listed in ${pair}, ${joinAnd(parts)}.`
}

/** Who came, went, or moved clinicians between the same rates: "87 left and 39 joined"; empty when nobody did. */
export function listedText(step: Pick<TrendStep, 'left' | 'joined' | 'reweighted'>): string {
  return joinAnd([
    step.left ? `${wholeNumber(step.left)} left` : '',
    step.joined ? `${wholeNumber(step.joined)} joined` : '',
    step.reweighted ? `${wholeNumber(step.reweighted)} moved clinicians between the same rates` : '',
  ])
}

function sum(steps: readonly TrendStep[], pick: (step: TrendStep) => number): number {
  return steps.reduce((total, step) => total + pick(step), 0)
}

/**
 * The group's typical price over the complete files, led by the providers listed in both (H1 of the #11 review). When
 * every one of them kept its rates, the move is never worded as a price change: "Of the 37 providers listed in both files,
 * all kept the same rate. 87 left and 39 joined, so the typical price on this card moved from $51.45 (Aug 31) to $114
 * (Sep 28)." Otherwise: "Of the 30 providers listed in both files, 28 kept the same rate, 1 got a higher rate and 1 a
 * lower one; 3 left. The typical price went from ..." Null when fewer than two files have a typical price.
 */
export function typicalTrendText(trend: GroupTrend, selectedId: number | null = null): string | null {
  const points = comparablePoints(trend).filter((point) => point.median != null)
  const values = points.map((point) => ({ snapshotId: point.snapshotId, date: point.indexDate, value: point.median! }))
  const movement = movementText('The typical price', values, 'typical', selectedId)
  if (!movement) return null
  const moves = typicalMoves(points)
  // No move, or a move the API can't explain (a file in between had no typical price): the amounts alone.
  if (!moves.length || moves.some((move) => !move.step)) return movement
  const steps = moves.map((move) => move.step!)
  const onlyTwo = points.length === 2

  if (steps.every(ratesKept)) {
    const lead = moves.map((move) => stayedText(move.step!, pairName(move, onlyTwo))).join(' ')
    const listed = listedText({ left: sum(steps, (s) => s.left), joined: sum(steps, (s) => s.joined), reweighted: sum(steps, (s) => s.reweighted) })
    const cause = listed ? `${capitalize(listed)}, so the typical price on this card` : 'So the typical price on this card'
    return `${lead} ${cause} ${wherePriceWas(values, selectedId)}`
  }
  const lead = moves
    .map((move) => {
      const listed = listedText(move.step!)
      const stayed = stayedText(move.step!, pairName(move, onlyTwo))
      return listed ? `${stayed.slice(0, -1)}; ${listed}.` : stayed
    })
    .join(' ')
  return `${lead} ${movement}`
}

/** The chart's tooltip for a file whose typical price moved since the one before: who stayed, and who came or went. */
export function stepNote(trend: GroupTrend, snapshotId: number): string | null {
  const points = comparablePoints(trend).filter((point) => point.median != null)
  const move = typicalMoves(points).find((candidate) => candidate.after.snapshotId === snapshotId)
  if (!move?.step) return null
  const stayed = stayedText(move.step, `the ${monthDay(move.before.indexDate)} file too`)
  const listed = listedText(move.step)
  return listed ? `${stayed} ${capitalize(listed)}.` : stayed
}

/** "Heel Sole Podiatry PC's rate"; a name ending in s takes only the apostrophe. */
export function rateOf(name: string): string {
  return /s$/i.test(name) ? `${name}' rate` : `${name}'s rate`
}

/**
 * The searched provider's rate over the complete files, naming the files where it has none. The subject is "Your rate"
 * only where the reader is the provider (the providers lens); a card names the provider ("Heel Sole Podiatry PC's rate").
 */
export function providerTrendText(trend: GroupTrend, subject: string, selectedId: number | null = null): string | null {
  const points = comparablePoints(trend)
  const values = points
    .filter((point) => point.provider)
    .map((point) => ({ snapshotId: point.snapshotId, date: point.indexDate, value: point.provider!.rate }))
  if (values.length === 0) return null
  const missing = points.filter((point) => !point.provider).map((point) => monthDay(point.indexDate))
  const absent = missing.length ? ` No rate in the ${missing.join(' or ')} file.` : ''
  if (values.length === 1) return `${subject}: ${text(values[0]!.value, 'rate')} (${monthDay(values[0]!.date)}).${absent}`
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
