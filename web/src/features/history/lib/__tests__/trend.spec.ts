import { describe, expect, it } from 'vitest'
import type { TrendStep } from '../../api/types'
import {
  drawableSeries,
  followedProvider,
  hasHistory,
  partialNote,
  providerByMonth,
  stepNote,
  trendSeries,
  typicalByMonth,
  whoLine,
  whoText,
} from '../trend'
import { point, trend } from './fixtures'

const yours = (rate: number, displayName = 'PM Pediatrics') => ({ providerKey: '061036715', displayName, rate })
const step = (sinceSnapshotId: number, counts: Partial<Omit<TrendStep, 'sinceSnapshotId'>>): TrendStep => ({
  sinceSnapshotId,
  stayed: 0,
  raised: 0,
  lowered: 0,
  reweighted: 0,
  left: 0,
  joined: 0,
  ...counts,
})

describe('trend readings', () => {
  it('gives the typical price by month, as the tiles show prices', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 51.45 }),
      point(2, '2026-08-31', { median: 51.45 }),
      point(1, '2026-09-28', { median: 113.59 }),
    ])
    expect(typicalByMonth(groupTrend)).toBe('Jul $51.45 · Aug $51.45 · Sep $114')
    expect(typicalByMonth(trend([point(1, '2026-09-28', { median: 100 })]))).toBeNull()
  })

  it("gives the searched provider's rate by month, to the cent, and the months without one", () => {
    const groupTrend = trend([point(3, '2026-07-27'), point(2, '2026-08-31', { provider: yours(100.36) }), point(1, '2026-09-28', { provider: yours(117.77) })])
    expect(providerByMonth(groupTrend)).toBe('Jul none · Aug $100.36 · Sep $117.77')
    expect(providerByMonth(trend([point(2, '2026-08-31'), point(1, '2026-09-28')]))).toBeNull()
  })

  it('says who stayed and who came or went for the last move, up to the month on screen', () => {
    // Ambetter 99213 office, modifier 25 (#11 review, H1): every provider in both files kept its rate.
    const h1 = trend([
      point(3, '2026-07-27', { median: 51.45 }),
      point(2, '2026-08-31', { median: 51.45, sincePrevious: step(3, { stayed: 122, left: 2, joined: 2 }) }),
      point(1, '2026-09-28', { median: 113.59, sincePrevious: step(2, { stayed: 37, left: 87, joined: 39 }) }),
    ])
    expect(whoLine(h1, 1)).toBe('Aug–Sep: 37 kept their rate, 87 left, 39 joined')
    // On the Aug 31 file the price hadn't moved yet.
    expect(whoLine(h1, 2)).toBeNull()
    expect(stepNote(h1, 1)).toBe('Since Aug: 37 kept their rate, 87 left, 39 joined')
    expect(stepNote(h1, 2)).toBeNull()
  })

  it('counts rates that went up or down, and clinicians moved between the same rates', () => {
    expect(whoText(step(2, { stayed: 30, raised: 1, lowered: 1, left: 3, joined: 1 }))).toBe('28 same rate, 1 up, 1 down, 3 left, 1 joined')
    expect(whoText(step(2, { stayed: 1, raised: 1 }))).toBe('1 up')
    expect(whoText(step(2, { stayed: 1 }))).toBe('1 kept its rate')
    expect(whoText(step(2, { stayed: 10, reweighted: 2 }))).toBe('10 kept their rate, 2 moved clinicians')
    expect(whoText(step(2, { left: 50, joined: 5 }))).toBe('none in both, 50 left, 5 joined')
  })

  it("says nothing about who moved when the API gives no step between the two files", () => {
    const gap = trend([point(3, '2026-07-27', { median: 100 }), point(1, '2026-09-28', { median: 120, sincePrevious: step(2, { joined: 4 }) })])
    expect(whoLine(gap, null)).toBeNull()
  })

  it('follows the provider by the name on the month on screen, else the newest', () => {
    const groupTrend = trend([point(2, '2026-08-31', { provider: yours(90, 'PM PEDIATRICS PC') }), point(1, '2026-09-28', { provider: yours(95) })])
    expect(followedProvider(groupTrend, 2)).toBe('PM PEDIATRICS PC')
    expect(followedProvider(groupTrend, 7)).toBe('PM Pediatrics')
    expect(followedProvider(trend([point(1, '2026-09-28')]), 1)).toBeNull()
  })

  it('leaves out incomplete files and says so in a few words', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 610, partial: true }),
      point(2, '2026-08-31', { median: null, statsLevel: 'list' }),
      point(1, '2026-09-28', { median: 620 }),
    ])
    expect(typicalByMonth(groupTrend)).toBeNull()
    expect(partialNote(groupTrend)).toBe('Jul file incomplete, left out')
    expect(partialNote(trend([point(3, '2026-07-27', { partial: true }), point(2, '2026-08-31', { partial: true })]))).toBe(
      'Jul and Aug files incomplete, left out',
    )
    expect(drawableSeries(groupTrend, false)).toBe(false)
  })

  it('never says median, percentile or quartile', () => {
    const groupTrend = trend([
      point(2, '2026-08-31', { median: 100, provider: yours(90) }),
      point(1, '2026-09-28', { median: 120, provider: yours(95), sincePrevious: step(2, { stayed: 5, raised: 3, joined: 1 }) }),
    ])
    const texts = [typicalByMonth(groupTrend), providerByMonth(groupTrend), whoLine(groupTrend, null), stepNote(groupTrend, 1)].join(' ')
    expect(texts.toLowerCase()).not.toMatch(/median|percentile|quartile/)
  })

  it('needs two snapshots for a history, and a line to draw for a chart', () => {
    expect(hasHistory(trend([point(1, '2026-09-28')]))).toBe(false)
    expect(hasHistory(trend([point(2, '2026-08-31'), point(1, '2026-09-28')]))).toBe(true)
    const small = trend([
      point(2, '2026-08-31', { median: null, provider: yours(300) }),
      point(1, '2026-09-28', { median: null, provider: yours(300) }),
    ])
    expect(drawableSeries(small, false)).toBe(false)
    expect(drawableSeries(small, true)).toBe(true)
  })

  it('draws the band most providers fall in (10+ providers), with gaps for incomplete files', () => {
    const series = trendSeries(
      trend([
        point(3, '2026-07-27', { partial: true }),
        point(2, '2026-08-31', { p10: 400, median: 500, p90: 650, provider: yours(480) }),
        point(1, '2026-09-28', { p10: null, median: 520, p90: null, statsLevel: 'basic' }),
      ]),
      2,
      true,
    )
    expect(series).toEqual({
      labels: ['Jul', 'Aug', 'Sep'],
      low: [null, 400, null],
      band: [null, 250, null],
      typical: [null, 500, 520],
      provider: [null, 480, null],
      selected: 1,
    })
  })
})
