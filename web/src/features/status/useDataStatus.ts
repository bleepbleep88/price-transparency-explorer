import { computed, onScopeDispose, shallowRef, watch } from 'vue'
import { createSharedComposable } from '@vueuse/core'
import { useQueryClient } from '@tanstack/vue-query'
import { buildUrl } from '@/lib/apiClient'
import { ratesApi } from '@/features/rates/api/ratesApi'
import type { IngestionStatus } from '@/features/rates/api/types'

/**
 * Live data-loading status, shared app-wide. The server pushes every change over server-sent events
 * (no polling). When loading finishes, cached queries are refreshed, so a search that got
 * "still loading" (503) re-runs by itself.
 */
export const useDataStatus = createSharedComposable(() => {
  const status = shallowRef<IngestionStatus | null>(null)
  const connected = shallowRef(false)
  const queryClient = useQueryClient()

  const source = typeof EventSource === 'undefined' ? null : new EventSource(buildUrl('/api/v1/status/stream'))
  source?.addEventListener('status', (event) => {
    connected.value = true
    status.value = JSON.parse((event as MessageEvent<string>).data) as IngestionStatus
  })
  // The browser reconnects an EventSource on its own; just reflect the state.
  source?.addEventListener('error', () => {
    connected.value = false
  })

  // First paint without waiting for the stream to connect.
  ratesApi
    .status()
    .then((initial) => {
      status.value ??= initial
    })
    .catch(() => undefined)

  watch(
    () => status.value?.phase,
    (phase, previous) => {
      if (phase === 'ready' && previous !== undefined && previous !== 'ready') {
        void queryClient.invalidateQueries()
      }
    },
  )

  onScopeDispose(() => source?.close())

  const dataAvailable = computed(() => status.value?.dataAvailable ?? false)

  /** Overall progress 0–1 across all files, or null when not loading. */
  const progress = computed(() => {
    const current = status.value
    if (!current || current.phase === 'ready' || current.phase === 'failed' || current.filesTotal === 0) return null
    return Math.min(1, (current.filesDone + (current.currentFileProgress ?? 0)) / current.filesTotal)
  })

  return { status, connected, dataAvailable, progress }
})
