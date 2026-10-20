import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import { historyApi, toTrendApiQuery } from '../api/historyApi'
import { historyKeys } from '../lib/historyKeys'

/** Each visible group across every snapshot; only worth asking for when more than one snapshot is loaded. */
export function useRateTrend(params: MaybeRefOrGetter<SearchParams>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery({
    queryKey: computed(() => historyKeys.trend(toTrendApiQuery(toValue(params)))),
    queryFn: ({ signal }) => historyApi.trend(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
  })
}
