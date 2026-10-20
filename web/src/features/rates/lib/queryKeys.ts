import type { QueryValue } from '@/lib/apiClient'

// Everything about rates nests under one prefix, so invalidating it refreshes every rate view at once.
export const rateKeys = {
  all: ['rates'] as const,
  // Keyed by the API query, so switching lens or typing a case count never refetches.
  search: (apiQuery: Record<string, QueryValue>) => [...rateKeys.all, 'search', apiQuery] as const,
  source: (rateId: number, provider: string | null) => [...rateKeys.all, 'source', rateId, provider] as const,
  npis: (rateIds: readonly number[], provider: string | null, page: number) => [...rateKeys.all, 'npis', rateIds, provider, page] as const,
  codes: (q: string, snapshot: number | undefined) => [...rateKeys.all, 'codes', q, snapshot] as const,
  providers: (q: string, snapshot: number | undefined) => [...rateKeys.all, 'providers', q, snapshot] as const,
}

export const datasetKeys = {
  all: ['dataset'] as const,
  snapshots: () => [...datasetKeys.all, 'snapshots'] as const,
  plans: (snapshotId: number) => [...datasetKeys.all, 'plans', snapshotId] as const,
  reference: () => [...datasetKeys.all, 'reference'] as const,
  specialties: (q: string) => [...datasetKeys.all, 'specialties', q] as const,
}

export const statusKeys = {
  current: () => ['status'] as const,
}
