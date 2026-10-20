import { describe, expect, it } from 'vitest'
import { drawableSeries, hasHistory, medianTrendText, movementText, partialNote, providerTrendText, trendSeries } from '../trend'
import { point, trend } from './fixtures'

const yours = (rate: number) => ({ providerKey: '061036715', displayName: 'PM Pediatrics', rate })

describe('trend readings', () => {
  it('follows the median through each file and says how far it moved since the first', () => {
    const text = medianTrendText(
      trend([point(3, '2026-07-27', { median: 51.45 }), point(2, '2026-08-31', { median: 51.45 }), point(1, '2026-09-28', { median: 113.59 })]),
    )
    expect(text).toBe('Median $51.45 (Jul 27) → $51.45 (Aug 31) → $113.59 (Sep 28), +120.8% since Jul 27.')
  })

  it('names the files when the amount never moves, and only the ends past three files', () => {
    expect(movementText('Median', [{ date: '2026-08-31', value: 80.72 }, { date: '2026-09-28', value: 80.72 }])).toBe(
      'Median $80.72 in the Aug 31 and Sep 28 files.',
    )
    const months = ['2026-06-29', '2026-07-27', '2026-08-31', '2026-09-28'].map((date, index) => ({ date, value: 100 + index }))
    expect(movementText('Median', months)).toBe('Median $100.00 (Jun 29) → $103.00 (Sep 28), +3.0% since Jun 29.')
    expect(movementText('Median', [{ date: '2026-09-28', value: 1 }])).toBeNull()
  })

  it('leaves out partial files and files too small for a median', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 610, partial: true }),
      point(2, '2026-08-31', { median: null, statsLevel: 'list' }),
      point(1, '2026-09-28', { median: 620 }),
    ])
    expect(medianTrendText(groupTrend)).toBeNull()
    expect(partialNote(groupTrend)).toBe("The Jul 27 file looks partial, so it's left out.")
    expect(drawableSeries(groupTrend, false)).toBe(false)
  })

  it("follows the searched provider's own rate and names the files without one", () => {
    const groupTrend = trend([
      point(3, '2026-07-27'),
      point(2, '2026-08-31', { provider: yours(100.36) }),
      point(1, '2026-09-28', { provider: yours(117.77) }),
    ])
    expect(providerTrendText(groupTrend)).toBe(
      'Your rate $100.36 (Aug 31) → $117.77 (Sep 28), +17.3% since Aug 31. No rate in the Jul 27 file.',
    )
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28', { provider: yours(80.72) })]), 'Your rate over time:')).toBe(
      'Your rate over time: $80.72 (Sep 28). No rate in the Jul 27 file.',
    )
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28')]))).toBeNull()
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

  it('gives the chart a band from the 25th to the 75th percentile, gaps for partial files, and the snapshot on screen', () => {
    const series = trendSeries(
      trend([
        point(3, '2026-07-27', { partial: true }),
        point(2, '2026-08-31', { p25: 400, median: 500, p75: 650, provider: yours(480) }),
        point(1, '2026-09-28', { p25: null, median: null, p75: null }),
      ]),
      2,
      true,
    )
    expect(series).toEqual({
      labels: ['Jul 27', 'Aug 31', 'Sep 28'],
      p25: [null, 400, null],
      band: [null, 250, null],
      median: [null, 500, null],
      provider: [null, 480, null],
      selected: 1,
    })
  })
})
