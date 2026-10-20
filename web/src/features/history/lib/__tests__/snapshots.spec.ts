import { describe, expect, it } from 'vitest'
import { currentSnapshot, olderSnapshots, partialProducts, previousSnapshot, snapshotLabel, snapshotNote, usableSnapshots } from '../snapshots'
import { product, snapshot, SNAPSHOTS } from './fixtures'

describe('snapshots', () => {
  it('lists snapshots with saved rates newest first, whatever their ids', () => {
    const pending = snapshot(4, '2026-10-26', { status: 'loading', products: [product({ status: 'pending', completeness: null })] })
    expect(usableSnapshots([SNAPSHOTS[2]!, pending, SNAPSHOTS[0]!, SNAPSHOTS[1]!]).map((s) => s.indexDate)).toEqual([
      '2026-09-28',
      '2026-08-31',
      '2026-07-27',
    ])
  })

  it('shows the one in the URL, else the newest', () => {
    expect(currentSnapshot(SNAPSHOTS, 2)?.indexDate).toBe('2026-08-31')
    expect(currentSnapshot(SNAPSHOTS, undefined)?.indexDate).toBe('2026-09-28')
    expect(currentSnapshot(SNAPSHOTS, 99)?.indexDate).toBe('2026-09-28')
    expect(currentSnapshot([], undefined)).toBeNull()
  })

  it('compares a snapshot with the ones before it', () => {
    expect(previousSnapshot(SNAPSHOTS, SNAPSHOTS[0]!)?.indexDate).toBe('2026-08-31')
    expect(previousSnapshot(SNAPSHOTS, SNAPSHOTS[2]!)).toBeNull()
    expect(olderSnapshots(SNAPSHOTS, SNAPSHOTS[0]!).map((s) => s.indexDate)).toEqual(['2026-08-31', '2026-07-27'])
  })

  it('labels dates short within one year and with the year across years', () => {
    expect(snapshotLabel(SNAPSHOTS[1]!, SNAPSHOTS)).toBe('Aug 31')
    expect(snapshotLabel(SNAPSHOTS[1]!, [...SNAPSHOTS, snapshot(9, '2025-12-29')])).toBe('Aug 31, 2026')
  })

  it('says which snapshot is the default and which products look incomplete', () => {
    const partial = snapshot(5, '2025-12-29', {
      products: [product({ completeness: { score: 0.1, rates: 0.1, eins: 0.4, codes: 0.6, comparedWith: [6, 7], partial: true } }), product({ productLabel: 'Essential Plan' })],
    })
    expect(snapshotNote(SNAPSHOTS[0]!)).toBe('Newest, shown by default')
    expect(partialProducts(partial)).toEqual(['Ambetter from Fidelis Care'])
    expect(snapshotNote(partial)).toBe('Ambetter from Fidelis Care looks incomplete')
    expect(snapshotNote(snapshot(4, '2026-10-26', { status: 'loading' }))).toBe('Still loading')
  })
})
