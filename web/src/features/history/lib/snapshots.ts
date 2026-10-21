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

/** Snapshots older than this one, newest first: what it can be compared with. */
export function olderSnapshots(snapshots: readonly SnapshotSummary[], snapshot: SnapshotSummary | null): SnapshotSummary[] {
  if (!snapshot?.indexDate) return []
  return usableSnapshots(snapshots).filter((other) => other.indexDate != null && other.indexDate < snapshot.indexDate!)
}

/** Product names whose file in this snapshot looks incomplete next to nearby snapshots. */
export function partialProducts(snapshot: SnapshotSummary): string[] {
  return snapshot.products.filter((product) => product.completeness?.partial).map((product) => product.productLabel)
}

/** One line under a month in the picker: "Newest", "Essential Plan looks incomplete", "Still loading". */
export function snapshotNote(snapshot: SnapshotSummary): string {
  const notes: string[] = []
  if (snapshot.isLatest) notes.push('Newest')
  const partial = partialProducts(snapshot)
  if (partial.length) notes.push(`${partial.join(' and ')} ${partial.length === 1 ? 'looks' : 'look'} incomplete`)
  if (snapshot.status === 'loading') notes.push('Still loading')
  return notes.join(' · ')
}
