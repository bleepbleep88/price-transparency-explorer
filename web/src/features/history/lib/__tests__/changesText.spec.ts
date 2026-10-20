import { describe, expect, it } from 'vitest'
import type { ChangeCounts } from '../../api/types'
import { changeAmounts, changesSinceText, changeText, countsSummary, listedElsewhereSummary } from '../changesText'
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
}
const none: ChangeCounts = { changed: 0, increased: 0, decreased: 0, new: 0, newListedElsewhere: 0, dropped: 0, droppedListedElsewhere: 0, unchanged: 12 }

describe('changes in words', () => {
  it('sums up what the filters cover: up, down, new and gone', () => {
    expect(countsSummary(counts, 'Aug 31, 2026', 'Sep 28, 2026')).toBe(
      'Between Aug 31, 2026 and Sep 28, 2026: 1,975 rates went up, 1,957 went down, 14,632 are new and 16,776 are gone. 41,163 rates stayed the same.',
    )
    expect(countsSummary({ ...none, new: 1 }, 'Aug 31, 2026', 'Sep 28, 2026')).toBe(
      'Between Aug 31, 2026 and Sep 28, 2026: 1 rate is new. 12 rates stayed the same.',
    )
    expect(countsSummary(none, 'Aug 31, 2026', 'Sep 28, 2026')).toBe('Nothing changed between Aug 31, 2026 and Sep 28, 2026. 12 rates stayed the same.')
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

  it("gives the plans lens one line for the code's changes since the last file", () => {
    expect(changesSinceText({ ...none, changed: 7, increased: 6, decreased: 1, new: 70, dropped: 14 }, 'Aug 31', '45378')).toBe(
      'Since the Aug 31 file, for 45378: 6 rates went up, 1 went down, 70 are new and 14 are gone.',
    )
    expect(changesSinceText({ ...none, changed: 1, increased: 1 }, 'Aug 31', '45378')).toBe('Since the Aug 31 file, for 45378: 1 rate went up.')
    expect(changesSinceText(none, 'Aug 31', '45378')).toBe('No rate for 45378 changed since the Aug 31 file.')
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
