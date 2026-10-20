import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { QueryValue } from '@/lib/apiClient'
import type { ChangeKind } from '../api/types'

// The changes page lives in the URL like search does. `snapshot` is the newer file (the app bar's snapshot, shared with
// search); `from` is the older one, which the API defaults to the snapshot just before.

export const CHANGE_KINDS = ['changed', 'new', 'dropped'] as const satisfies readonly ChangeKind[]
export const CHANGE_SORTS = ['largest', '-changePct', 'changePct', 'code', '-code', 'provider', '-provider'] as const
export type ChangeSort = (typeof CHANGE_SORTS)[number]
export const DEFAULT_CHANGE_SORT: ChangeSort = 'largest'
export const DEFAULT_CHANGES_PAGE_SIZE = 25
export const MAX_CHANGE_PCT = 1_000

export interface ChangesParams {
  snapshot?: number
  from?: number
  product?: string
  code?: string
  kind?: ChangeKind
  minChangePct?: number
  /** Leave out new and dropped rates the other file lists under other modifiers or another setting. */
  hideListedElsewhere: boolean
  sort: ChangeSort
  page: number
  pageSize: number
}

export type ChangesPatch = Partial<Record<keyof ChangesParams, string | number | boolean | null | undefined>>

function first(value: LocationQuery[string] | undefined): string | undefined {
  const raw = Array.isArray(value) ? value[0] : value
  const trimmed = raw?.trim()
  return trimmed ? trimmed : undefined
}

function positiveInteger(value: string | undefined, max = Number.MAX_SAFE_INTEGER): number | undefined {
  const number = Number(value)
  return Number.isInteger(number) && number >= 1 && number <= max ? number : undefined
}

function oneOf<T extends string>(options: readonly T[], value: string | undefined): T | undefined {
  return (options as readonly string[]).includes(value ?? '') ? (value as T) : undefined
}

/** Tolerant like the search's parser: unknown or malformed values fall back to defaults. */
export function parseChangesQuery(query: LocationQuery): ChangesParams {
  const minChangePct = Number(first(query.minChangePct))
  return {
    snapshot: positiveInteger(first(query.snapshot)),
    from: positiveInteger(first(query.from)),
    product: first(query.product),
    code: first(query.code),
    kind: oneOf(CHANGE_KINDS, first(query.kind)),
    minChangePct: first(query.minChangePct) && minChangePct > 0 && minChangePct <= MAX_CHANGE_PCT ? minChangePct : undefined,
    hideListedElsewhere: first(query.listedElsewhere) === 'false',
    sort: oneOf(CHANGE_SORTS, first(query.sort)) ?? DEFAULT_CHANGE_SORT,
    page: positiveInteger(first(query.page)) ?? 1,
    pageSize: positiveInteger(first(query.pageSize), 100) ?? DEFAULT_CHANGES_PAGE_SIZE,
  }
}

/** Applies a patch to the current query; null, '' or false removes a key, and defaults are dropped to keep URLs short. */
export function mergeChangesQuery(current: LocationQuery, patch: ChangesPatch): LocationQueryRaw {
  const next: LocationQueryRaw = { ...current }
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined) continue
    const name = key === 'hideListedElsewhere' ? 'listedElsewhere' : key
    if (value === null || value === '' || value === false) delete next[name]
    else next[name] = key === 'hideListedElsewhere' ? 'false' : String(value)
  }
  if (next.sort === DEFAULT_CHANGE_SORT) delete next.sort
  if (next.page === '1') delete next.page
  if (next.pageSize === String(DEFAULT_CHANGES_PAGE_SIZE)) delete next.pageSize
  return next
}

/** What the API takes: the page's snapshot is the newer file (`to`). */
export function toChangesApiQuery(params: ChangesParams): Record<string, QueryValue> {
  return {
    from: params.from,
    to: params.snapshot,
    product: params.product,
    code: params.code,
    kind: params.kind,
    minChangePct: params.minChangePct,
    listedElsewhere: params.hideListedElsewhere ? false : undefined,
    sort: params.sort === DEFAULT_CHANGE_SORT ? undefined : params.sort,
    page: params.page,
    pageSize: params.pageSize,
  }
}
