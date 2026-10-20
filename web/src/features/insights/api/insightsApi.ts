import { requestJson } from '@/lib/apiClient'
import { compareApiQuery, gapApiQuery, type CompareParams, type GapParams } from '../lib/insightParams'
import type { GapLeaderboard, ProductComparison, ProviderScorecard } from './types'

export const insightsApi = {
  gaps: (params: GapParams, signal?: AbortSignal) =>
    requestJson<GapLeaderboard>('/api/v1/insights/gaps', { query: gapApiQuery(params), signal }),

  /** `provider` is an EIN or a provider key from results; the response names the key it resolved to. */
  scorecard: (provider: string, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<ProviderScorecard>(`/api/v1/insights/providers/${encodeURIComponent(provider)}`, { query: { snapshot }, signal }),

  compare: (params: CompareParams, signal?: AbortSignal) =>
    requestJson<ProductComparison>('/api/v1/insights/compare', { query: compareApiQuery(params), signal }),
}
