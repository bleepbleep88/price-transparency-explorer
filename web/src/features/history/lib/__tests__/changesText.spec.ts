import { describe, expect, it } from 'vitest'
import type { ChangeCounts } from '../../api/types'
import {
  changeAmounts,
  changeText,
  listedElsewhereSummary,
  priceMove,
  upDown,
  waitingText,
} from '../changesText'
import { change } from './fixtures'

// The real 2026-08-31 → 2026-09-28 counts.
const counts: ChangeCounts = {
  changed: 3932,
  increased: 1975,
  decreased: 1957,
  new: 14632,
  newListedElsewhere: 5858,
  dropped: 16776,
  droppedListedElsewhere: 5856,
  unchanged: 41163,
  facilityCharges: 2210,
}
const none: ChangeCounts = { changed: 0, increased: 0, decreased: 0, new: 0, newListedElsewhere: 0, dropped: 0, droppedListedElsewhere: 0, unchanged: 12, facilityCharges: 0 }

describe('changes in words', () => {
  it("gives a row's prices in a few characters: old to new, new or gone", () => {
    expect(priceMove(change({ from: 42, to: 193.19 }))).toBe('$42 → $193')
    expect(priceMove(change({ from: 3.36, to: 10.7 }))).toBe('$3.36 → $10.70')
    expect(priceMove(change({ from: 100.21, to: 100.4 }))).toBe('$100.21 → $100.40')
    expect(priceMove(change({ kind: 'new', from: null, to: 750 }))).toBe('New · $750')
    expect(priceMove(change({ kind: 'dropped', from: 450, to: null }))).toBe('Gone · $450')
  })

  it('says up or down by the amounts as shown', () => {
    expect(upDown(51.45, 53.2, 'typical')).toBe('Up $1.75')
    expect(upDown(113.01, 113.59, 'typical')).toBe('Up $1')
    expect(upDown(113.4, 113.01, 'typical')).toBeNull()
    expect(upDown(100.36, 117.77)).toBe('Up $17.41')
    expect(upDown(71.38, 61.65)).toBe('Down $9.73')
  })

  it('says why there are no changes yet, naming the files', () => {
    const side = (snapshotId: number, indexDate: string, statisticsBuiltAt: string | null, statisticsCurrent: boolean) => ({
      snapshotId,
      indexDate,
      isLatest: false,
      statisticsBuiltAt,
      statisticsCurrent,
    })
    expect(waitingText({ from: side(2, '2026-08-31', null, false), to: side(1, '2026-09-28', '2026-10-20T08:00:00Z', true) })).toBe(
      'Changes show once loading finishes: the numbers for Aug 31, 2026 are still being saved.',
    )
    expect(
      waitingText({ from: side(2, '2026-08-31', '2026-10-20T08:00:00Z', false), to: side(1, '2026-09-28', '2026-10-20T08:00:00Z', false) }),
    ).toBe('Changes show once loading finishes: the numbers for Aug 31, 2026 and Sep 28, 2026 are being updated.')
  })

  it('says how many new and gone rates are only listed differently', () => {
    expect(listedElsewhereSummary(counts)).toBe(
      '5,858 of the new and 5,856 of the gone rates are the same provider and service, listed with other modifiers or in another setting.',
    )
    expect(listedElsewhereSummary({ ...counts, newListedElsewhere: 0 })).toBe(
      '5,856 of the gone rates are the same provider and service, listed with other modifiers or in another setting.',
    )
    expect(listedElsewhereSummary(none)).toBeNull()
  })

  it('shows a change in dollars and percent, and big jumps as how many times as much', () => {
    expect(changeText(change())).toBe('Up $60 (10%)')
    expect(changeText(change({ from: 500, to: 450, change: -50, changePct: -10 }))).toBe('Down $50 (10%)')
    expect(changeText(change({ from: 20.18, to: 1667.07, change: 1646.89, changePct: 8161 }))).toBe('Up $1,646.89 (about 83 times as much)')
    expect(changeText(change({ from: 0, to: 50, change: 50, changePct: null }))).toBe('Up $50')
    expect(changeText(change({ kind: 'new', from: null, change: null, changePct: null }))).toBe('New')
    expect(changeText(change({ kind: 'dropped', to: null, change: null, changePct: null }))).toBe('Gone')
  })

  it('works the change out from the amounts as shown, so they add up', () => {
    expect(changeAmounts(100.36, 117.77)).toEqual({ from: '$100.36', to: '$117.77', change: 'up $17.41 (17%)' })
    expect(changeAmounts(113.01, 113.59)).toEqual({ from: '$113.01', to: '$113.59', change: 'up $0.58 (under 1%)' })
    expect(changeAmounts(1500, 1200)).toEqual({ from: '$1,500.00', to: '$1,200.00', change: 'down $300 (20%)' })
    // Typical prices as cards show them: whole dollars from $100, and the change from those figures.
    expect(changeAmounts(113.01, 113.59, 'typical')).toEqual({ from: '$113', to: '$114', change: 'up $1 (under 1%)' })
    expect(changeAmounts(51.45, 51.45, 'typical').change).toBe('no change')
  })
})
