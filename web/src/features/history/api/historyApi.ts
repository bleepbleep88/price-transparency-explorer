import { requestJson } from '@/lib/apiClient'
import type { QueryValue } from '@/lib/apiClient'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { RateTrendResult } from './types'

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
    snapshot: params.snapshot,
  }
}

export const historyApi = {
  trend: (params: SearchParams, signal?: AbortSignal) =>
    requestJson<RateTrendResult>('/api/v1/rates/trend', { query: toTrendApiQuery(params), signal }),
}
