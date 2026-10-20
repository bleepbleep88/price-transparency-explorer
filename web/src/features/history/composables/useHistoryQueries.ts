import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import { historyApi, toTrendApiQuery } from '../api/historyApi'
import { toChangesApiQuery, type ChangesParams } from '../lib/changesParams'
import { historyKeys } from '../lib/historyKeys'

export function useChanges(params: MaybeRefOrGetter<ChangesParams>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery({
    queryKey: computed(() => historyKeys.changes(toChangesApiQuery(toValue(params)))),
    queryFn: ({ signal }) => historyApi.changes(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
    // The previous page stays on screen (dimmed) while the next one loads.
    placeholderData: keepPreviousData,
  })
}

/** Each visible group across every snapshot; only worth asking for when more than one snapshot is loaded. */
export function useRateTrend(params: MaybeRefOrGetter<SearchParams>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery({
    queryKey: computed(() => historyKeys.trend(toTrendApiQuery(toValue(params)))),
    queryFn: ({ signal }) => historyApi.trend(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
  })
}
