import { monthDay, shortDate } from '@/lib/format'
import type { SnapshotSummary } from '@/features/rates/api/types'

// Which snapshot a page shows, and how the picker names each one.

function hasRates(snapshot: SnapshotSummary): boolean {
  return snapshot.products.some((product) => product.status === 'loaded' || product.status === 'partial')
}

function byDateNewestFirst(a: SnapshotSummary, b: SnapshotSummary): number {
  return (b.indexDate ?? '').localeCompare(a.indexDate ?? '') || b.snapshotId - a.snapshotId
}

/** Snapshots with saved rates (the ones searches can use), newest first. */
export function usableSnapshots(snapshots: readonly SnapshotSummary[]): SnapshotSummary[] {
  return snapshots.filter(hasRates).sort(byDateNewestFirst)
}

/** The snapshot in the URL, else the one searches default to (the newest). */
export function currentSnapshot(snapshots: readonly SnapshotSummary[], id: number | undefined): SnapshotSummary | null {
  return snapshots.find((snapshot) => snapshot.snapshotId === id) ?? snapshots.find((snapshot) => snapshot.isLatest) ?? null
}

/** The newest snapshot older than this one, or null for the oldest. */
export function previousSnapshot(snapshots: readonly SnapshotSummary[], snapshot: SnapshotSummary | null): SnapshotSummary | null {
  if (!snapshot?.indexDate) return null
  return usableSnapshots(snapshots).find((other) => other.indexDate != null && other.indexDate < snapshot.indexDate!) ?? null
}

/** Snapshots older than this one, newest first: what it can be compared with. */
export function olderSnapshots(snapshots: readonly SnapshotSummary[], snapshot: SnapshotSummary | null): SnapshotSummary[] {
  if (!snapshot?.indexDate) return []
  return usableSnapshots(snapshots).filter((other) => other.indexDate != null && other.indexDate < snapshot.indexDate!)
}

/** "Aug 31" when every snapshot is in one year, else "Aug 31, 2026". */
export function snapshotLabel(snapshot: SnapshotSummary | null, all: readonly SnapshotSummary[]): string {
  if (!snapshot) return '—'
  const years = new Set(all.map((other) => other.indexDate?.slice(0, 4)))
  return years.size <= 1 ? monthDay(snapshot.indexDate) : shortDate(snapshot.indexDate)
}

/** Product names whose file in this snapshot looks partial next to nearby snapshots. */
export function partialProducts(snapshot: SnapshotSummary): string[] {
  return snapshot.products.filter((product) => product.completeness?.partial).map((product) => product.productLabel)
}

/** One line under a snapshot in the picker: "Newest", "Essential Plan looks partial", "Still loading". */
export function snapshotNote(snapshot: SnapshotSummary): string {
  const notes: string[] = []
  if (snapshot.isLatest) notes.push('Newest, shown by default')
  const partial = partialProducts(snapshot)
  if (partial.length) notes.push(`${partial.join(' and ')} ${partial.length === 1 ? 'looks' : 'look'} partial`)
  if (snapshot.status === 'loading') notes.push('Still loading')
  return notes.join(' · ')
}
