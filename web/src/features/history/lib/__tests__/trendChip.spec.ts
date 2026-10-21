import { describe, expect, it } from 'vitest'
import type { TrendStep } from '../../api/types'
import { trendChipText } from '../trendChip'
import { point, trend } from './fixtures'

const JUL = 3
const AUG = 2
const SEP = 1

/**
 * How the providers moved since the file before: rates changed with the same providers (priced), rates changed while
 * providers also joined or left (mixed), or only who is listed changed (listed).
 */
const priced = (since: number): TrendStep => ({ sinceSnapshotId: since, stayed: 20, raised: 3, lowered: 1, reweighted: 0, left: 0, joined: 0 })
const mixed = (since: number): TrendStep => ({ sinceSnapshotId: since, stayed: 24, raised: 1, lowered: 0, reweighted: 0, left: 2, joined: 2 })
const listed = (since: number): TrendStep => ({ sinceSnapshotId: since, stayed: 37, raised: 0, lowered: 0, reweighted: 0, left: 87, joined: 39 })

/** Three monthly files with these typical prices; each step's providers as given. */
function months(prices: [number, number, number], steps: [TrendStep | null, TrendStep | null] = [priced(JUL), priced(AUG)]) {
  return trend([
    point(JUL, '2026-07-27', { median: prices[0] }),
    point(AUG, '2026-08-31', { median: prices[1], sincePrevious: steps[0] }),
    point(SEP, '2026-09-28', { median: prices[2], sincePrevious: steps[1] }),
  ])
}

describe('trendChipText', () => {
  it('says rates are steady when the typical price never moved', () => {
    expect(trendChipText(months([100, 100, 100]), SEP)).toBe('Rates steady since Jul')
  })

  it("never calls a move a price change when every provider listed in both files kept its rate", () => {
    // Ambetter 99213 office, modifier 25: $51.45 to $113.59, while all 37 providers in both files kept their rate.
    expect(trendChipText(months([51.45, 51.45, 113.59], [priced(JUL), listed(AUG)]), SEP)).toBe('Rates steady since Jul')
    expect(trendChipText(months([100, 150, 150], [listed(JUL), priced(AUG)]), SEP)).toBe('Rates steady since Jul')
  })

  it('gives the move since the file it started from, as the tile shows prices', () => {
    expect(trendChipText(months([100, 100, 112]), SEP)).toBe('Up $12 since Aug')
    expect(trendChipText(months([100, 112, 112]), SEP)).toBe('Up $12 since Jul')
    expect(trendChipText(months([100, 110, 120]), SEP)).toBe('Up $20 since Jul')
    expect(trendChipText(months([62, 71, 62]), SEP)).toBe('Down $9 since Aug')
    expect(trendChipText(months([51.45, 51.45, 53.2]), SEP)).toBe('Up $1.75 since Aug')
    // Typical prices from $100 show whole dollars, so $113.01 to $113.59 ($113 to $114) is up $1.
    expect(trendChipText(months([113.01, 113.01, 113.59]), SEP)).toBe('Up $1 since Aug')
  })

  it('leaves out of the amount any move that only reflects who is listed', () => {
    expect(trendChipText(months([100, 150, 160], [listed(JUL), priced(AUG)]), SEP)).toBe('Up $10 since Aug')
    expect(trendChipText(months([100, 120, 150], [priced(JUL), listed(AUG)]), SEP)).toBe('Rates up since Jul')
  })

  it('gives no amount when providers also joined or left in the move', () => {
    // Essential Plan 45378 surgery center, Aug to Sep: 24 of 25 kept their rate, 1 went up, 2 left and 2 joined.
    expect(trendChipText(months([496, 481, 511], [priced(JUL), mixed(AUG)]), SEP)).toBe('Rates up since Aug')
    expect(trendChipText(months([100, 110, 120], [mixed(JUL), priced(AUG)]), SEP)).toBe('Rates up since Jul')
    expect(trendChipText(months([120, 110, 100], [priced(JUL), mixed(AUG)]), SEP)).toBe('Rates down since Jul')
  })

  it('agrees with the file on screen: only the files up to it count', () => {
    expect(trendChipText(months([100, 112, 130]), AUG)).toBe('Up $12 since Jul')
    expect(trendChipText(months([100, 112, 130]), null)).toBe('Up $30 since Jul')
    expect(trendChipText(months([100, 112, 130]), JUL)).toBeNull()
  })

  it('skips incomplete files, and treats a move it cannot explain as a price move', () => {
    const partialMiddle = trend([
      point(JUL, '2026-07-27', { median: 100 }),
      point(AUG, '2026-08-31', { median: 40, partial: true }),
      point(SEP, '2026-09-28', { median: 112, sincePrevious: priced(JUL) }),
    ])
    expect(trendChipText(partialMiddle, SEP)).toBe('Up $12 since Jul')
    // August had too few providers for a typical price, so the step the API gives is from August, not July.
    const gap = trend([
      point(JUL, '2026-07-27', { median: 100 }),
      point(AUG, '2026-08-31', { median: null, statsLevel: 'list' }),
      point(SEP, '2026-09-28', { median: 120, sincePrevious: listed(AUG) }),
    ])
    expect(trendChipText(gap, SEP)).toBe('Up $20 since Jul')
  })

  it('has nothing to say without history', () => {
    expect(trendChipText(null, SEP)).toBeNull()
    expect(trendChipText(trend([point(SEP, '2026-09-28', { median: 100 })]), SEP)).toBeNull()
    expect(trendChipText(months([100, 100, 100]), 99)).toBeNull()
    const incompleteOnScreen = trend([point(AUG, '2026-08-31', { median: 100 }), point(SEP, '2026-09-28', { median: 50, partial: true })])
    expect(trendChipText(incompleteOnScreen, SEP)).toBeNull()
    const tooFewOnScreen = trend([point(AUG, '2026-08-31', { median: 100 }), point(SEP, '2026-09-28', { median: null, statsLevel: 'list' })])
    expect(trendChipText(tooFewOnScreen, SEP)).toBeNull()
  })

  it('stays a few words with no statistics words', () => {
    const chips = [
      trendChipText(months([100, 100, 100]), SEP),
      trendChipText(months([51.45, 51.45, 113.59], [priced(JUL), listed(AUG)]), SEP),
      trendChipText(months([100, 110, 120]), SEP),
      trendChipText(months([100, 120, 150], [priced(JUL), listed(AUG)]), SEP),
    ]
    for (const chip of chips) {
      expect(chip!.split(' ').length).toBeLessThanOrEqual(5)
      expect(chip!.toLowerCase()).not.toMatch(/median|percentile|quartile|typical|snapshot|index/)
    }
  })
})
