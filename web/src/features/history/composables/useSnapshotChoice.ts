import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSnapshots } from '@/features/rates/composables/useRateQueries'
import type { SnapshotSummary } from '@/features/rates/api/types'
import { currentSnapshot, usableSnapshots } from '../lib/snapshots'

/**
 * The snapshot every page shows: `snapshot` in the URL, else the newest. Picking one keeps the rest of the page's query
 * (a new snapshot starts at page 1, and the changes page compares it with the one before it again).
 */
export function useSnapshotChoice() {
  const route = useRoute()
  const router = useRouter()
  const query = useSnapshots()

  const snapshots = computed(() => usableSnapshots(query.data.value ?? []))
  const requestedId = computed(() => {
    const raw = Array.isArray(route.query.snapshot) ? route.query.snapshot[0] : route.query.snapshot
    const id = Number(raw)
    return Number.isInteger(id) && id > 0 ? id : undefined
  })
  const current = computed(() => currentSnapshot(snapshots.value, requestedId.value))

  /** The query to carry the snapshot to another page: empty when it's the default one. */
  const carried = computed(() => {
    const query: Record<string, string> = {}
    if (requestedId.value) query.snapshot = String(requestedId.value)
    return query
  })

  function choose(snapshot: SnapshotSummary) {
    const next = { ...route.query }
    delete next.page
    delete next.from
    if (snapshot.isLatest) delete next.snapshot
    else next.snapshot = String(snapshot.snapshotId)
    return router.push({ path: route.path, query: next })
  }

  return { snapshots, current, requestedId, carried, choose, isPending: query.isPending }
}
