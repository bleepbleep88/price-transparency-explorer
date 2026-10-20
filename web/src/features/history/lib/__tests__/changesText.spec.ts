import { describe, expect, it } from 'vitest'
import type { ChangeCounts } from '../../api/types'
import { changesSinceText, changeText, countsSummary, listedElsewhereSummary } from '../changesText'
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
  it('sums up what the filters cover', () => {
    expect(countsSummary(counts, 'Aug 31, 2026', 'Sep 28, 2026')).toBe(
      'Between Aug 31, 2026 and Sep 28, 2026: 3,932 rates changed (1,975 up, 1,957 down), 14,632 new and 16,776 dropped. 41,163 rates stayed the same.',
    )
    expect(countsSummary(none, 'Aug 31, 2026', 'Sep 28, 2026')).toBe('Nothing changed between Aug 31, 2026 and Sep 28, 2026. 12 rates stayed the same.')
  })

  it('says how many new and dropped rates are only listed differently', () => {
    expect(listedElsewhereSummary(counts)).toBe(
      '5,858 of the new and 5,856 of the dropped are the same provider and code listed under other modifiers or another setting.',
    )
    expect(listedElsewhereSummary({ ...counts, newListedElsewhere: 0 })).toBe(
      '5,856 of the dropped are the same provider and code listed under other modifiers or another setting.',
    )
    expect(listedElsewhereSummary(none)).toBeNull()
  })

  it("gives the plans lens one line for the code's changes since the last file", () => {
    expect(changesSinceText({ ...none, changed: 7, increased: 6, decreased: 1, new: 70, dropped: 14 }, 'Aug 31', '45378')).toBe(
      'Since the Aug 31 file, for 45378: 7 rates changed (6 up, 1 down), 70 new and 14 dropped.',
    )
    expect(changesSinceText({ ...none, changed: 1, increased: 1 }, 'Aug 31', '45378')).toBe(
      'Since the Aug 31 file, for 45378: 1 rate changed (1 up, 0 down).',
    )
    expect(changesSinceText(none, 'Aug 31', '45378')).toBe('No rate for 45378 changed since the Aug 31 file.')
  })

  it('shows a change in dollars and percent, and new or dropped rates as such', () => {
    expect(changeText(change())).toBe('+$60.00 (+10.0%)')
    expect(changeText(change({ change: -50, changePct: -10 }))).toBe('−$50.00 (−10.0%)')
    expect(changeText(change({ from: 0, change: 50, changePct: null }))).toBe('+$50.00')
    expect(changeText(change({ kind: 'new', from: null, change: null, changePct: null }))).toBe('New')
    expect(changeText(change({ kind: 'dropped', to: null, change: null, changePct: null }))).toBe('Dropped')
  })
})
