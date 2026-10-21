import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { insightsApi } from '../api/insightsApi'
import { compareApiQuery, gapApiQuery, type CompareParams, type GapParams } from '../lib/insightParams'

// All insights nest under one prefix; the data status refreshes every query when a load or rebuild finishes.
export const insightKeys = {
  all: ['insights'] as const,
  gaps: (params: GapParams) => [...insightKeys.all, 'gaps', gapApiQuery(params)] as const,
  scorecard: (provider: string, snapshot: number | undefined, includeFacilityCharges = false) =>
    [...insightKeys.all, 'scorecard', provider, snapshot, includeFacilityCharges] as const,
  compare: (params: CompareParams) => [...insightKeys.all, 'compare', compareApiQuery(params)] as const,
}

export function useGapLeaderboard(params: MaybeRefOrGetter<GapParams>, enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: computed(() => insightKeys.gaps(toValue(params))),
    queryFn: ({ signal }) => insightsApi.gaps(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
    // Keep the current page on screen (dimmed) while the next page or filter loads.
    placeholderData: keepPreviousData,
  })
}

export function useProviderScorecard(
  provider: MaybeRefOrGetter<string>,
  snapshot: MaybeRefOrGetter<number | undefined>,
  includeFacilityCharges: MaybeRefOrGetter<boolean> = false,
) {
  return useQuery({
    queryKey: computed(() => insightKeys.scorecard(toValue(provider), toValue(snapshot), toValue(includeFacilityCharges))),
    queryFn: ({ signal }) => insightsApi.scorecard(toValue(provider), toValue(snapshot), toValue(includeFacilityCharges), signal),
    enabled: computed(() => toValue(provider) !== ''),
    // Turning facility charges on or off keeps the page on screen while the other answer loads.
    placeholderData: keepPreviousData,
  })
}

export function useProductComparison(params: MaybeRefOrGetter<CompareParams>, enabled: MaybeRefOrGetter<boolean> = true) {
  return useQuery({
    queryKey: computed(() => insightKeys.compare(toValue(params))),
    queryFn: ({ signal }) => insightsApi.compare(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
    placeholderData: keepPreviousData,
  })
}
