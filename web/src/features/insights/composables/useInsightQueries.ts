import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { insightsApi } from '../api/insightsApi'
import { compareApiQuery, gapApiQuery, type CompareParams, type GapParams } from '../lib/insightParams'

// All insights nest under one prefix; the data status refreshes every query when a load or rebuild finishes.
export const insightKeys = {
  all: ['insights'] as const,
  gaps: (params: GapParams) => [...insightKeys.all, 'gaps', gapApiQuery(params)] as const,
  scorecard: (provider: string, snapshot: number | undefined) => [...insightKeys.all, 'scorecard', provider, snapshot] as const,
  compare: (params: CompareParams) => [...insightKeys.all, 'compare', compareApiQuery(params)] as const,
}

export function useGapLeaderboard(params: MaybeRefOrGetter<GapParams>) {
  return useQuery({
    queryKey: computed(() => insightKeys.gaps(toValue(params))),
    queryFn: ({ signal }) => insightsApi.gaps(toValue(params), signal),
    // Keep the current page on screen (dimmed) while the next page or filter loads.
    placeholderData: keepPreviousData,
  })
}

export function useProviderScorecard(provider: MaybeRefOrGetter<string>, snapshot: MaybeRefOrGetter<number | undefined>) {
  return useQuery({
    queryKey: computed(() => insightKeys.scorecard(toValue(provider), toValue(snapshot))),
    queryFn: ({ signal }) => insightsApi.scorecard(toValue(provider), toValue(snapshot), signal),
    enabled: computed(() => toValue(provider) !== ''),
  })
}

export function useProductComparison(params: MaybeRefOrGetter<CompareParams>) {
  return useQuery({
    queryKey: computed(() => insightKeys.compare(toValue(params))),
    queryFn: ({ signal }) => insightsApi.compare(toValue(params), signal),
    placeholderData: keepPreviousData,
  })
}
