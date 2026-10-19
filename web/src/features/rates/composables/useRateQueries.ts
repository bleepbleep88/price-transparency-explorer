import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { refDebounced } from '@vueuse/core'
import { ratesApi } from '../api/ratesApi'
import { datasetKeys, rateKeys } from '../lib/queryKeys'
import type { SearchParams } from '../lib/searchParams'

export function useRateSearch(params: MaybeRefOrGetter<SearchParams>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery({
    queryKey: computed(() => rateKeys.search(toValue(params))),
    queryFn: ({ signal }) => ratesApi.search(toValue(params), signal),
    enabled: computed(() => toValue(enabled)),
    // Keep showing the previous results (dimmed) while the next page or filter loads.
    placeholderData: keepPreviousData,
  })
}

export function useRateSource(rateId: MaybeRefOrGetter<number | null>) {
  return useQuery({
    queryKey: computed(() => rateKeys.source(toValue(rateId) ?? 0)),
    queryFn: ({ signal }) => ratesApi.source(toValue(rateId)!, signal),
    enabled: computed(() => toValue(rateId) != null),
  })
}

export function useRateNpis(
  rateId: MaybeRefOrGetter<number | null>,
  provider: MaybeRefOrGetter<string | null>,
  page: MaybeRefOrGetter<number>,
  pageSize = 50,
) {
  return useQuery({
    queryKey: computed(() => rateKeys.npis(toValue(rateId) ?? 0, toValue(provider), toValue(page))),
    queryFn: ({ signal }) => ratesApi.npis(toValue(rateId)!, toValue(provider), toValue(page), pageSize, signal),
    enabled: computed(() => toValue(rateId) != null),
    placeholderData: keepPreviousData,
  })
}

/** Typeahead over codes that have rates. Debounced so each keystroke doesn't hit the API. */
export function useCodeSuggestions(search: MaybeRefOrGetter<string>, snapshot: MaybeRefOrGetter<number | undefined>) {
  const debounced = refDebounced(computed(() => (toValue(search) ?? '').trim()), 250)
  return useQuery({
    queryKey: computed(() => rateKeys.codes(debounced.value, toValue(snapshot))),
    queryFn: ({ signal }) => ratesApi.codes(debounced.value, toValue(snapshot), signal),
    enabled: computed(() => debounced.value.length >= 1),
    placeholderData: keepPreviousData,
  })
}

export function useProviderSuggestions(search: MaybeRefOrGetter<string>, snapshot: MaybeRefOrGetter<number | undefined>) {
  const debounced = refDebounced(computed(() => (toValue(search) ?? '').trim()), 250)
  return useQuery({
    queryKey: computed(() => rateKeys.providers(debounced.value, toValue(snapshot))),
    queryFn: ({ signal }) => ratesApi.providers(debounced.value, toValue(snapshot), signal),
    enabled: computed(() => debounced.value.length >= 2),
    placeholderData: keepPreviousData,
  })
}

export function useSnapshots() {
  return useQuery({ queryKey: datasetKeys.snapshots(), queryFn: ({ signal }) => ratesApi.snapshots(signal) })
}

export function usePlans(snapshotId: MaybeRefOrGetter<number | null>) {
  return useQuery({
    queryKey: computed(() => datasetKeys.plans(toValue(snapshotId) ?? 0)),
    queryFn: ({ signal }) => ratesApi.plans(toValue(snapshotId)!, signal),
    enabled: computed(() => toValue(snapshotId) != null),
  })
}

export function useReferenceLists() {
  return useQuery({ queryKey: datasetKeys.reference(), queryFn: ({ signal }) => ratesApi.reference(signal) })
}
