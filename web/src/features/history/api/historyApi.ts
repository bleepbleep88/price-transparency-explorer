import { requestJson } from '@/lib/apiClient'
import type { QueryValue } from '@/lib/apiClient'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import { toChangesApiQuery, type ChangesParams } from '../lib/changesParams'
import type { ChangesResult, RateTrendResult } from './types'

/** The search inputs a trend takes: everything that decides which groups show, nothing about rows or paging. */
export function toTrendApiQuery(params: SearchParams): Record<string, QueryValue> {
  return {
    code: params.code,
    codeType: params.codeType,
    npi: params.npi,
    ein: params.ein,
    product: params.product,
    setting: params.setting,
    feeType: params.feeType,
    provider: params.provider,
    specialty: params.specialty,
    county: params.county,
    snapshot: params.snapshot,
  }
}

export const historyApi = {
  changes: (params: ChangesParams, signal?: AbortSignal) =>
    requestJson<ChangesResult>('/api/v1/changes', { query: toChangesApiQuery(params), signal }),

  trend: (params: SearchParams, signal?: AbortSignal) =>
    requestJson<RateTrendResult>('/api/v1/rates/trend', { query: toTrendApiQuery(params), signal }),
}
