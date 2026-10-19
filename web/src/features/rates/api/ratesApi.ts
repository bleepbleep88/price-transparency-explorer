import { requestJson } from '@/lib/apiClient'
import type { SearchParams } from '../lib/searchParams'
import { toApiQuery } from '../lib/searchParams'
import type {
  CodeSuggestion,
  IngestionStatus,
  ProviderSuggestion,
  RateNpisPage,
  RateSearchResult,
  RateSource,
  ReferenceLists,
  SnapshotPlans,
  SnapshotSummary,
} from './types'

export const ratesApi = {
  search: (params: SearchParams, signal?: AbortSignal) =>
    requestJson<RateSearchResult>('/api/v1/rates', { query: toApiQuery(params), signal }),

  source: (rateId: number, signal?: AbortSignal) =>
    requestJson<RateSource>(`/api/v1/rates/${rateId}/source`, { signal }),

  npis: (rateId: number, provider: string | null, page: number, pageSize: number, signal?: AbortSignal) =>
    requestJson<RateNpisPage>(`/api/v1/rates/${rateId}/npis`, { query: { provider, page, pageSize }, signal }),

  codes: (q: string, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<CodeSuggestion[]>('/api/v1/codes', { query: { q, snapshot, limit: 8 }, signal }),

  providers: (q: string, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<ProviderSuggestion[]>('/api/v1/providers', { query: { q, snapshot, limit: 8 }, signal }),

  reference: (signal?: AbortSignal) => requestJson<ReferenceLists>('/api/v1/reference', { signal }),

  snapshots: (signal?: AbortSignal) => requestJson<SnapshotSummary[]>('/api/v1/snapshots', { signal }),

  plans: (snapshotId: number, signal?: AbortSignal) =>
    requestJson<SnapshotPlans>(`/api/v1/snapshots/${snapshotId}/plans`, { signal }),

  status: (signal?: AbortSignal) => requestJson<IngestionStatus>('/api/v1/status', { signal }),
}
