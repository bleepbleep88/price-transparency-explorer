import { useQuery } from '@tanstack/vue-query'
import { sourcesApi } from '../api/sourcesApi'

export const sourceKeys = {
  all: ['sources'] as const,
  list: () => [...sourceKeys.all, 'list'] as const,
}

/** Refreshed with every other query when loading finishes (see useDataStatus). */
export function useSources() {
  return useQuery({ queryKey: sourceKeys.list(), queryFn: ({ signal }) => sourcesApi.list(signal) })
}
