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
  SpecialtySuggestion,
} from './types'

export const ratesApi = {
  search: (params: SearchParams, signal?: AbortSignal) =>
    requestJson<RateSearchResult>('/api/v1/rates', { query: toApiQuery(params), signal }),


  /** Where a rate came from; with `provider` (the row's provider key), that provider's groups are listed first. */
  source: (rateId: number, provider: string | null, signal?: AbortSignal) =>
    requestJson<RateSource>(`/api/v1/rates/${rateId}/source`, { query: { provider }, signal }),
  /** NPIs behind a result row: `rateId` plus the row's other rates (`alsoRateIds`), under one provider. */
  npis: (rateId: number, alsoRateIds: readonly number[], provider: string | null, page: number, pageSize: number, signal?: AbortSignal) =>
    requestJson<RateNpisPage>(`/api/v1/rates/${rateId}/npis`, { query: { provider, alsoRate: alsoRateIds, page, pageSize }, signal }),

  codes: (q: string, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<CodeSuggestion[]>('/api/v1/codes', { query: { q, snapshot, limit: 8 }, signal }),

  providers: (q: string, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<ProviderSuggestion[]>('/api/v1/providers', { query: { q, snapshot, limit: 8 }, signal }),

  /** Specialties from the national provider registry that the specialty filter matches. */
  specialties: (q: string, signal?: AbortSignal) =>
    requestJson<SpecialtySuggestion[]>('/api/v1/specialties', { query: { q, limit: 8 }, signal }),

  reference: (signal?: AbortSignal) => requestJson<ReferenceLists>('/api/v1/reference', { signal }),

  snapshots: (signal?: AbortSignal) => requestJson<SnapshotSummary[]>('/api/v1/snapshots', { signal }),

  plans: (snapshotId: number, signal?: AbortSignal) =>
    requestJson<SnapshotPlans>(`/api/v1/snapshots/${snapshotId}/plans`, { signal }),

  status: (signal?: AbortSignal) => requestJson<IngestionStatus>('/api/v1/status', { signal }),
}
