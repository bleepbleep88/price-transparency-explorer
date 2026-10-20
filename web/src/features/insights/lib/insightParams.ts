import { z } from 'zod'
import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { QueryValue } from '@/lib/apiClient'
import { FEE_TYPES, type FeeType } from '@/features/rates/lib/searchParams'
import type { CompareSort, GapSort } from '../api/types'

// Each insights page keeps its filters in the URL, like search: shareable, bookmarkable, and Back works.

export const GAP_SORTS = ['gap', 'spread'] as const satisfies readonly GapSort[]
export const COMPARE_SORTS = ['difference', 'ratio', '-ratio'] as const satisfies readonly CompareSort[]
export const INSIGHT_PAGE_SIZES = [10, 25, 50, 100] as const
export const DEFAULT_INSIGHT_PAGE_SIZE = 25

export interface GapParams {
  product?: string
  category?: string
  setting?: string
  feeType?: FeeType
  code?: string
  /** Also rank charges filed under a revenue code with no procedure named (off by default: not like for like). */
  includeFacilityCharges?: boolean
  sort: GapSort
  page: number
  pageSize: number
  snapshot?: number
}

export interface CompareParams {
  product?: string
  versus?: string
  code?: string
  category?: string
  setting?: string
  sort: CompareSort
  page: number
  pageSize: number
  snapshot?: number
}

/** Keys a filter or link can change; null or '' removes the parameter. */
export type InsightPatch = Partial<Record<string, string | number | null | undefined>>

function first(value: LocationQuery[string] | undefined): string | undefined {
  const raw = Array.isArray(value) ? value[0] : value
  const trimmed = raw?.trim()
  return trimmed ? trimmed : undefined
}

function positiveInteger(value: string | undefined, max = Number.MAX_SAFE_INTEGER): number | undefined {
  const number = Number(value)
  return Number.isInteger(number) && number >= 1 && number <= max ? number : undefined
}

const gapSort = z.enum(GAP_SORTS).catch('gap')
const compareSort = z.enum(COMPARE_SORTS).catch('difference')

/** Tolerant: unknown or malformed values fall back to defaults instead of breaking the page. */
export function parseGapQuery(query: LocationQuery): GapParams {
  const feeType = first(query.feeType)
  return {
    product: first(query.product),
    category: first(query.category),
    setting: first(query.setting),
    feeType: (FEE_TYPES as readonly string[]).includes(feeType ?? '') ? (feeType as FeeType) : undefined,
    code: first(query.code),
    includeFacilityCharges: first(query.includeFacilityCharges) === 'true' || undefined,
    sort: gapSort.parse(first(query.sort)),
    page: positiveInteger(first(query.page)) ?? 1,
    pageSize: positiveInteger(first(query.pageSize), 100) ?? DEFAULT_INSIGHT_PAGE_SIZE,
    snapshot: positiveInteger(first(query.snapshot)),
  }
}

export function parseCompareQuery(query: LocationQuery): CompareParams {
  return {
    product: first(query.product),
    versus: first(query.versus),
    code: first(query.code),
    category: first(query.category),
    setting: first(query.setting),
    sort: compareSort.parse(first(query.sort)),
    page: positiveInteger(first(query.page)) ?? 1,
    pageSize: positiveInteger(first(query.pageSize), 100) ?? DEFAULT_INSIGHT_PAGE_SIZE,
    snapshot: positiveInteger(first(query.snapshot)),
  }
}

export function gapApiQuery(params: GapParams): Record<string, QueryValue> {
  return { ...params }
}

export function compareApiQuery(params: CompareParams): Record<string, QueryValue> {
  return { ...params }
}

/**
 * The products to compare after picking `key` for one side. Picking the product the other side already shows swaps the
 * two, so the comparison never sets a product against itself.
 */
export function pickProducts(
  current: { product: string; versus: string },
  side: 'product' | 'versus',
  key: string,
): { product: string; versus: string } {
  const other = side === 'product' ? current.versus : current.product
  if (key === other) return { product: current.versus, versus: current.product }
  return side === 'product' ? { product: key, versus: current.versus } : { product: current.product, versus: key }
}

/**
 * Applies a patch to the current query. null or '' removes a key; defaults are dropped to keep URLs short. Changing
 * anything but the page starts again at page 1.
 */
export function mergeInsightQuery(current: LocationQuery, patch: InsightPatch, defaultSort: string): LocationQueryRaw {
  const next: LocationQueryRaw = { ...current }
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined) continue
    if (value === null || value === '') delete next[key]
    else next[key] = String(value)
  }
  if (!('page' in patch)) delete next.page
  if (next.sort === defaultSort) delete next.sort
  if (next.page === '1') delete next.page
  if (next.pageSize === String(DEFAULT_INSIGHT_PAGE_SIZE)) delete next.pageSize
  return next
}
