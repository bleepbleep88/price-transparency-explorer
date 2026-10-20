import { describe, expect, it } from 'vitest'
import type { TrendStep } from '../../api/types'
import {
  drawableSeries,
  followedProvider,
  hasHistory,
  movementText,
  partialNote,
  providerTrendText,
  rateOf,
  stepNote,
  trendSeries,
  typicalTrendText,
} from '../trend'
import { point, trend } from './fixtures'

const yours = (rate: number, displayName = 'PM Pediatrics') => ({ providerKey: '061036715', displayName, rate })
const at = (snapshotId: number, date: string, value: number) => ({ snapshotId, date, value })
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
  it('names every file of three, as the cards show them, and sums up a move that went one way', () => {
    const text = typicalTrendText(
      trend([point(3, '2026-07-27', { median: 51.45 }), point(2, '2026-08-31', { median: 51.45 }), point(1, '2026-09-28', { median: 113.59 })]),
      1,
    )
    // Cards show $113.59 as "$114", so the change is worked out from $114.
    expect(text).toBe('The typical price was $51.45 (Jul 27), $51.45 (Aug 31) and $114 (Sep 28): up $62.55 (about 2.2 times as much) since Jul 27.')
  })

  it("never says a price didn't change when the file on screen says otherwise", () => {
    const values = [at(3, '2026-07-27', 62), at(2, '2026-08-31', 71), at(1, '2026-09-28', 62)]

    expect(movementText('The typical price', values, 'typical', 2)).toBe('The typical price was $62 (Jul 27), $71 (Aug 31, this file) and $62 (Sep 28).')
    expect(movementText('The typical price', values, 'typical', 1)).not.toMatch(/no change|didn't change/)
  })

  it('gives the ends of more than three files, and the one on screen', () => {
    const values = [at(4, '2026-06-29', 100), at(3, '2026-07-27', 110), at(2, '2026-08-31', 90), at(1, '2026-09-28', 120)]

    expect(movementText('The typical price', values, 'typical', 2)).toBe(
      'The typical price went from $100 (Jun 29) to $120 (Sep 28): up $20 (20%). In the Aug 31 file on screen it was $90.',
    )
    expect(movementText('The typical price', values, 'typical', 1)).toBe('The typical price went from $100 (Jun 29) to $120 (Sep 28): up $20 (20%).')
  })

  it('names the files when the price never moves, and shows rates to the cent', () => {
    expect(movementText('Your rate', [at(2, '2026-08-31', 80.72), at(1, '2026-09-28', 80.72)])).toBe('Your rate was $80.72 in the Aug 31 and Sep 28 files.')
    expect(movementText('Your rate', [at(2, '2026-08-31', 600), at(1, '2026-09-28', 660)])).toBe(
      'Your rate went from $600.00 (Aug 31) to $660.00 (Sep 28): up $60 (10%).',
    )
    const months = ['2026-06-29', '2026-07-27', '2026-08-31', '2026-09-28'].map((date, index) => at(4 - index, date, 100))
    expect(movementText('The typical price', months, 'typical')).toBe('The typical price was $100 in all 4 files.')
    expect(movementText('The typical price', [at(2, '2026-08-31', 113.4), at(1, '2026-09-28', 113.01)], 'typical')).toBe(
      'The typical price was $113 in the Aug 31 and Sep 28 files.',
    )
    expect(movementText('The typical price', [at(1, '2026-09-28', 1)])).toBeNull()
  })

  it('says small moves plainly', () => {
    expect(movementText('The typical price', [at(3, '2026-07-27', 113.01), at(1, '2026-09-28', 113.59)], 'typical')).toBe(
      'The typical price went from $113 (Jul 27) to $114 (Sep 28): up $1 (under 1%).',
    )
    expect(movementText('The typical price', [at(3, '2026-07-27', 573.48), at(1, '2026-09-28', 568.65)], 'typical')).toBe(
      'The typical price went from $573 (Jul 27) to $569 (Sep 28): down $4 (under 1%).',
    )
    expect(movementText('Your rate', [at(3, '2026-07-27', 113.01), at(1, '2026-09-28', 113.59)])).toBe(
      'Your rate went from $113.01 (Jul 27) to $113.59 (Sep 28): up $0.58 (under 1%).',
    )
  })

  it('never calls a move a price change when every provider listed in both files kept its rate', () => {
    // Ambetter 99213 office, modifier 25, 08-31 to 09-28 (#11 review, H1).
    const groupTrend = trend([
      point(2, '2026-08-31', { median: 51.45 }),
      point(1, '2026-09-28', { median: 113.59, sincePrevious: step(2, { stayed: 37, left: 87, joined: 39 }) }),
    ])

    const text = typicalTrendText(groupTrend, 1)

    expect(text).toBe(
      'Of the 37 providers listed in both files, all kept the same rate. 87 left and 39 joined, so the typical price on this card moved from $51.45 (Aug 31) to $114 (Sep 28).',
    )
    expect(text).not.toMatch(/went up|up \$|times as much/)
    expect(stepNote(groupTrend, 1)).toBe('Of the 37 providers listed in the Aug 31 file too, all kept the same rate. 87 left and 39 joined.')
  })

  it('leads with each step that moved, and names every one of three files', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 167.09 }),
      point(2, '2026-08-31', { median: 205.41, sincePrevious: step(3, { stayed: 5, left: 50 }) }),
      point(1, '2026-09-28', { median: 190, sincePrevious: step(2, { stayed: 4, left: 1, joined: 2 }) }),
    ])

    expect(typicalTrendText(groupTrend, 2)).toBe(
      'Of the 5 providers listed in the Jul 27 and Aug 31 files, all kept the same rate. Of the 4 providers listed in the Aug 31 and Sep 28 files, all kept the same rate. 51 left and 2 joined, so the typical price on this card was $167 (Jul 27), $205 (Aug 31, this file) and $190 (Sep 28).',
    )
  })

  it('says how many providers got a higher or lower rate, then how the typical price moved', () => {
    const groupTrend = trend([
      point(3, '2026-07-27', { median: 100 }),
      point(2, '2026-08-31', { median: 110, sincePrevious: step(3, { stayed: 20, raised: 5, lowered: 2, left: 3 }) }),
      point(1, '2026-09-28', { median: 110, sincePrevious: step(2, { stayed: 20, raised: 9, lowered: 9, joined: 4 }) }),
    ])

    // The Aug 31 to Sep 28 step left the typical price where it was, so it doesn't explain the move.
    expect(typicalTrendText(groupTrend)).toBe(
      'Of the 20 providers listed in the Jul 27 and Aug 31 files, 13 kept the same rate, 5 got a higher rate and 2 a lower one; 3 left. The typical price was $100 (Jul 27), $110 (Aug 31) and $110 (Sep 28): up $10 (10%) since Jul 27.',
    )
    expect(
      typicalTrendText(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 150, sincePrevious: step(2, { stayed: 1, raised: 1, joined: 3 }) })])),
    ).toBe('The one provider listed in both files got a higher rate; 3 joined. The typical price went from $100 (Aug 31) to $150 (Sep 28): up $50 (50%).')
  })

  it('names clinicians moving between the same rates, and files with no provider in common', () => {
    expect(
      typicalTrendText(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 120, sincePrevious: step(2, { stayed: 10, reweighted: 2 }) })])),
    ).toBe(
      'Of the 10 providers listed in both files, all kept the same rate. 2 moved clinicians between the same rates, so the typical price on this card moved from $100 (Aug 31) to $120 (Sep 28).',
    )
    expect(
      typicalTrendText(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 150, sincePrevious: step(2, { left: 50, joined: 5 }) })])),
    ).toBe('No provider is listed in both files. 50 left and 5 joined, so the typical price on this card moved from $100 (Aug 31) to $150 (Sep 28).')
  })

  it("gives only the amounts when the price didn't move or a step is missing", () => {
    expect(
      typicalTrendText(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 100, sincePrevious: step(2, { left: 4 }) })])),
    ).toBe('The typical price was $100 in the Aug 31 and Sep 28 files.')
    // A step from another file (one in between had no typical price) is another pair.
    expect(
      typicalTrendText(trend([point(3, '2026-07-27', { median: 100 }), point(1, '2026-09-28', { median: 120, sincePrevious: step(2, { joined: 4 }) })])),
    ).toBe('The typical price went from $100 (Jul 27) to $120 (Sep 28): up $20 (20%).')
    expect(stepNote(trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 100, sincePrevious: step(2, { left: 4 }) })]), 1)).toBeNull()
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

  it("follows the searched provider's own rate under the subject given, and names the files without one", () => {
    const groupTrend = trend([
      point(3, '2026-07-27'),
      point(2, '2026-08-31', { provider: yours(100.36) }),
      point(1, '2026-09-28', { provider: yours(117.77) }),
    ])
    expect(providerTrendText(groupTrend, 'Your rate')).toBe(
      'Your rate went from $100.36 (Aug 31) to $117.77 (Sep 28): up $17.41 (17%). No rate in the Jul 27 file.',
    )
    expect(providerTrendText(groupTrend, rateOf('Heel Sole Podiatry PC'))).toBe(
      "Heel Sole Podiatry PC's rate went from $100.36 (Aug 31) to $117.77 (Sep 28): up $17.41 (17%). No rate in the Jul 27 file.",
    )
    expect(rateOf('PM PEDIATRICS')).toBe("PM PEDIATRICS' rate")
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28', { provider: yours(80.72) })]), 'Your rate')).toBe(
      'Your rate: $80.72 (Sep 28). No rate in the Jul 27 file.',
    )
    expect(providerTrendText(trend([point(3, '2026-07-27'), point(1, '2026-09-28')]), 'Your rate')).toBeNull()
  })

  it('follows the provider by the name on the card on screen, else the newest', () => {
    const groupTrend = trend([point(2, '2026-08-31', { provider: yours(90, 'PM PEDIATRICS PC') }), point(1, '2026-09-28', { provider: yours(95) })])

    expect(followedProvider(groupTrend, 2)).toBe('PM PEDIATRICS PC')
    expect(followedProvider(groupTrend, 7)).toBe('PM Pediatrics')
    expect(followedProvider(trend([point(1, '2026-09-28')]), 1)).toBeNull()
  })

  it('never says median, percentile or quartile', () => {
    const texts = [
      typicalTrendText(
        trend([point(2, '2026-08-31', { median: 100 }), point(1, '2026-09-28', { median: 120, sincePrevious: step(2, { raised: 3, joined: 1 }) })]),
      ),
      providerTrendText(trend([point(2, '2026-08-31', { provider: yours(90) }), point(1, '2026-09-28', { provider: yours(95) })]), 'Your rate'),
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
