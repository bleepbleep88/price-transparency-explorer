import { z } from 'zod'
import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { QueryValue } from '@/lib/apiClient'
import { isEinOrProviderKey, isValidNpi, npiDigits } from './providerIds'

// The URL query string is the single source of truth for a search: shareable, bookmarkable, and Back works.

export const SORTS = ['rate', '-rate', 'provider', '-provider', 'vsMedian', '-vsMedian'] as const
export type SortKey = (typeof SORTS)[number]
export const DEFAULT_PAGE_SIZE = 25
export const FEE_TYPES = ['facility', 'professional'] as const
export type FeeType = (typeof FEE_TYPES)[number]

export interface SearchParams {
  code: string
  codeType?: string
  npi?: string
  ein?: string
  product?: string
  setting?: string
  feeType?: FeeType
  provider?: string
  group?: string
  sort: SortKey
  page: number
  pageSize: number
  snapshot?: number
}

/** Keys a link or chip can change. null removes the parameter. */
export type QueryPatch = Partial<Record<keyof SearchParams, string | number | null | undefined>>

function first(value: LocationQuery[string] | undefined): string | undefined {
  const raw = Array.isArray(value) ? value[0] : value
  const trimmed = raw?.trim()
  return trimmed ? trimmed : undefined
}

function positiveInteger(value: string | undefined, max = Number.MAX_SAFE_INTEGER): number | undefined {
  const number = Number(value)
  return Number.isInteger(number) && number >= 1 && number <= max ? number : undefined
}

/** Tolerant: unknown or malformed values fall back to defaults instead of breaking the page. */
export function parseRouteQuery(query: LocationQuery): SearchParams {
  const sort = first(query.sort)
  const feeType = first(query.feeType)
  return {
    code: first(query.code) ?? '',
    codeType: first(query.codeType),
    npi: first(query.npi),
    ein: first(query.ein),
    product: first(query.product),
    setting: first(query.setting),
    feeType: (FEE_TYPES as readonly string[]).includes(feeType ?? '') ? (feeType as FeeType) : undefined,
    provider: first(query.provider),
    group: first(query.group),
    sort: (SORTS as readonly string[]).includes(sort ?? '') ? (sort as SortKey) : 'rate',
    page: positiveInteger(first(query.page)) ?? 1,
    pageSize: positiveInteger(first(query.pageSize), 100) ?? DEFAULT_PAGE_SIZE,
    snapshot: positiveInteger(first(query.snapshot)),
  }
}

/** Applies a patch to the current query. null or '' removes a key; defaults are dropped to keep URLs short. */
export function mergeQuery(current: LocationQuery, patch: QueryPatch): LocationQueryRaw {
  const next: LocationQueryRaw = { ...current }
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined) continue
    if (value === null || value === '') delete next[key]
    else next[key] = String(value)
  }
  if (next.sort === 'rate') delete next.sort
  if (next.page === '1') delete next.page
  if (next.pageSize === String(DEFAULT_PAGE_SIZE)) delete next.pageSize
  return next
}

export function toApiQuery(params: SearchParams): Record<string, QueryValue> {
  return {
    code: params.code,
    codeType: params.codeType,
    npi: params.npi,
    ein: params.ein,
    product: params.product,
    setting: params.setting,
    feeType: params.feeType,
    provider: params.provider,
    group: params.group,
    sort: params.sort,
    page: params.page,
    pageSize: params.pageSize,
    snapshot: params.snapshot,
  }
}

// ---- Form rules (same wording as the API's validator) ----

export const searchFormSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).')
    .max(8, 'A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.')
    .regex(/^[A-Za-z0-9]+$/, 'A billing code is only letters and digits, for example 45378, J0585 or 0490.'),
  npi: z
    .string()
    .trim()
    .refine((value) => value === '' || (/^[\d\s-]+$/.test(value) && npiDigits(value).length === 10), {
      error: 'An NPI is 10 digits, for example 1396747861.',
    }),
  ein: z
    .string()
    .trim()
    .refine((value) => value === '' || isEinOrProviderKey(value), { error: 'An EIN is 9 digits, for example 11-3471596.' }),
  provider: z
    .string()
    .trim()
    .refine((value) => value === '' || (value.length >= 2 && value.length <= 100), {
      error: "Type at least 2 characters of the provider's name.",
    }),
})

export type SearchFormValues = z.input<typeof searchFormSchema>
export type FormField = keyof SearchFormValues
export type FormErrors = Partial<Record<FormField, string>>

export function validateSearchForm(values: SearchFormValues): FormErrors {
  const result = searchFormSchema.safeParse(values)
  if (result.success) return {}
  const errors: FormErrors = {}
  for (const issue of result.error.issues) {
    const field = issue.path[0] as FormField
    errors[field] ??= issue.message
  }
  return errors
}

/** Non-blocking: real plan files publish a few NPIs that fail the check digit, so search still runs. */
export function npiCheckDigitWarning(npi: string | null | undefined): string | null {
  return npiDigits(npi).length === 10 && !isValidNpi(npi)
    ? 'This NPI fails the standard check-digit test. Some plan files publish NPIs like this, so you can still search.'
    : null
}

export function formValuesFrom(params: SearchParams): SearchFormValues {
  return { code: params.code, npi: params.npi ?? '', ein: params.ein ?? '', provider: params.provider ?? '' }
}
