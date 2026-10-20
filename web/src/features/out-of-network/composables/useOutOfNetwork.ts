import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import { outOfNetworkApi } from '../api/outOfNetworkApi'

export const outOfNetworkKeys = {
  all: ['out-of-network'] as const,
  forCode: (code: string, codeType: string | undefined, snapshot: number | undefined) =>
    [...outOfNetworkKeys.all, code.toUpperCase(), codeType ?? null, snapshot ?? null] as const,
}

/**
 * The searched code's out-of-network payments. Keyed by code, code type and snapshot only, so paging, sorting and
 * filters never refetch it. No placeholder: a new code shows nothing until its own answer arrives.
 */
export function useOutOfNetwork(params: MaybeRefOrGetter<SearchParams>, enabled: MaybeRefOrGetter<boolean>) {
  return useQuery({
    queryKey: computed(() => {
      const { code, codeType, snapshot } = toValue(params)
      return outOfNetworkKeys.forCode(code.trim(), codeType, snapshot)
    }),
    queryFn: ({ signal }) => {
      const { code, codeType, snapshot } = toValue(params)
      return outOfNetworkApi.forCode(code.trim(), codeType, snapshot, signal)
    },
    enabled: computed(() => toValue(enabled)),
  })
}
