import type { QueryValue } from '@/lib/apiClient'

// Keyed by the API query, so a lens or a page of the search table never refetches a trend.
export const historyKeys = {
  all: ['history'] as const,
  changes: (apiQuery: Record<string, QueryValue>) => [...historyKeys.all, 'changes', apiQuery] as const,
  trend: (apiQuery: Record<string, QueryValue>) => [...historyKeys.all, 'trend', apiQuery] as const,
}
