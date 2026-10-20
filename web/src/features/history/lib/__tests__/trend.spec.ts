import { describe, expect, it } from 'vitest'
import { drawableSeries, hasHistory, movementText, partialNote, providerTrendText, trendSeries, typicalTrendText } from '../trend'
import { point, trend } from './fixtures'

const yours = (rate: number) => ({ providerKey: '061036715', displayName: 'PM Pediatrics', rate })

describe('trend readings', () => {
  it('says how the typical price moved from the first file to the last, as the cards show it', () => {
    const text = typicalTrendText(
      trend([point(3, '2026-07-27', { median: 51.45 }), point(2, '2026-08-31', { median: 51.45 }), point(1, '2026-09-28', { median: 113.59 })]),
    )
    // Cards show $113.59 as "$114", so the change is worked out from $114.
    expect(text).toBe('The typical price went from $51.45 (Jul 27) to $114 (Sep 28): up $62.55 (about 2.2 times as much).')
  })

  it('names the files when the price never moves, and shows rates to the cent', () => {
    expect(movementText('Your rate', [{ date: '2026-08-31', value: 80.72 }, { date: '2026-09-28', value: 80.72 }])).toBe(
      'Your rate was $80.72 in the Aug 31 and Sep 28 files.',
    )
    expect(movementText('Your rate', [{ date: '2026-08-31', value: 600 }, { date: '2026-09-28', value: 660 }])).toBe(
      'Your rate went from $600.00 (Aug 31) to $660.00 (Sep 28): up $60 (10%).',
    )
    const months = ['2026-06-29', '2026-07-27', '2026-08-31', '2026-09-28'].map((date) => ({ date, value: 100 }))
    expect(movementText('The typical price', months, 'typical')).toBe('The typical price was $100 in all 4 files.')
    expect(movementText('The typical price', [{ date: '2026-08-31', value: 113.4 }, { date: '2026-09-28', value: 113.01 }], 'typical')).toBe(
      'The typical price was $113 in the Aug 31 and Sep 28 files.',
    )
    expect(movementText('The typical price', [{ date: '2026-09-28', value: 1 }])).toBeNull()
  })

  it('says small moves plainly', () => {
    expect(movementText('The typical price', [{ date: '2026-07-27', value: 113.01 }, { date: '2026-09-28', value: 113.59 }], 'typical')).toBe(
      'The typical price went from $113 (Jul 27) to $114 (Sep 28): up $1 (under 1%).',
    )
    expect(movementText('The typical price', [{ date: '2026-07-27', value: 573.48 }, { date: '2026-09-28', value: 568.65 }], 'typical')).toBe(
      'The typical price went from $573 (Jul 27) to $569 (Sep 28): down $4 (under 1%).',
    )
    expect(movementText('Your rate', [{ date: '2026-07-27', value: 113.01 }, { date: '2026-09-28', value: 113.59 }])).toBe(
      'Your rate went from $113.01 (Jul 27) to $113.59 (Sep 28): up $0.58 (under 1%).',
    )
  })

  it('leaves out incomplete files and files too small for a typical price', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 610, partial: true }),
      point(2, '2026-08-31', { median: null, statsLevel: 'list' }),
      point(1, '2026-09-28', { median: 620 }),
    ])
    expect(typicalTrendText(groupTrend)).toBeNull()
    expect(partialNote(groupTrend)).toBe("The Jul 27 file looks incomplete, so it's left out.")
    expect(drawableSeries(groupTrend, false)).toBe(false)
  })

  it("follows the searched provider's own rate and names the files without one", () => {
    const groupTrend = trend([
      point(3, '2026-07-27'),
      point(2, '2026-08-31', { provider: yours(100.36) }),
      point(1, '2026-09-28', { provider: yours(117.77) }),
    ])
    expect(providerTrendText(groupTrend)).toBe('Your rate went from $100.36 (Aug 31) to $117.77 (Sep 28): up $17.41 (17%). No rate in the Jul 27 file.')
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28', { provider: yours(80.72) })]))).toBe(
      'Your rate: $80.72 (Sep 28). No rate in the Jul 27 file.',
    )
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28')]))).toBeNull()
  })

  it('never says median, percentile or quartile', () => {
    const texts = [
      typicalTrendText(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 120 })])),
      providerTrendText(trend([point(2, '2026-08-31', { provider: yours(90) }), point(1, '2026-09-28', { provider: yours(95) })])),
      partialNote(trend([point(2, '2026-08-31', { partial: true }), point(1, '2026-09-28')])),
    ].join(' ')
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
      labels: ['Jul 27', 'Aug 31', 'Sep 28'],
      low: [null, 400, null],
      band: [null, 250, null],
      typical: [null, 500, 520],
      provider: [null, 480, null],
      selected: 1,
    })
  })
})
