import { requestJson } from '@/lib/apiClient'
import type { SourceSnapshot } from './types'

export const sourcesApi = {
  /** Every snapshot and data file, newest first, with how each was loaded and cleaned. */
  list: (signal?: AbortSignal) => requestJson<SourceSnapshot[]>('/api/v1/sources', { signal }),
}
