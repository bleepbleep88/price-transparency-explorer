import { z } from 'zod'
import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { QueryValue } from '@/lib/apiClient'
import { isEinOrProviderKey, isValidNpi, normalizeEin, npiDigits } from './providerIds'

// The URL query string is the single source of truth for a search: shareable, bookmarkable, and Back works.

export const SORTS = ['rate', '-rate', 'provider', '-provider', 'vsMedian', '-vsMedian'] as const
export type SortKey = (typeof SORTS)[number]
export const DEFAULT_PAGE_SIZE = 25
export const FEE_TYPES = ['facility', 'professional'] as const
export type FeeType = (typeof FEE_TYPES)[number]

/** Audience lenses: one neutral explorer, plus an interpretation for whoever is reading. */
export const LENSES = ['providers', 'plans', 'employers', 'innovators', 'life-sciences'] as const
export const lensSchema = z.enum(LENSES)
export type Lens = z.infer<typeof lensSchema>
/** Upper bound for the employers lens's cases-a-year input. */
export const MAX_CASES = 1_000_000

export interface SearchParams {
  code: string
  codeType?: string
  npi?: string
  ein?: string
  product?: string
  setting?: string
  feeType?: FeeType
  provider?: string
  /** A specialty name from the typeahead, or words matched against the start of words in specialty names, e.g. "gastro". */
  specialty?: string
  /** A New York county or NYC borough from the reference list, e.g. "Manhattan" or "Monroe". */
  county?: string
  group?: string
  sort: SortKey
  page: number
  pageSize: number
  snapshot?: number
  /** Interpretation only: never sent to the API, and it survives new searches. */
  lens?: Lens
  /** The plan tab picked on the results (a product key). Interpretation only: never sent to the API. */
  plan?: string
  /** Employers lens: cases a year for the searched code; a search for a different code drops it. */
  cases?: number
  /** Dose calculator: a dose of the searched drug, in its code's unit; a search for a different code drops it. */
  dose?: number
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

/** Decimals a dose can have. */
export const DOSE_DECIMALS = 6

/**
 * A dose as typed: a plain decimal ("45", "2.5", ".5"), thousands commas allowed ("2,400,000"), up to 6 decimals.
 * No signs, exponents or hex ("1e3", "0x10"). Null for anything else, and for 0.
 */
export function parseDose(text: string | null | undefined): number | null {
  const trimmed = (text ?? '').trim()
  if (!/^(?:\d{1,3}(?:,\d{3})+|\d*)(?:\.\d+)?$/.test(trimmed) || !/\d/.test(trimmed)) return null
  const [, decimals = ''] = trimmed.split('.')
  if (decimals.length > DOSE_DECIMALS) return null
  const value = Number(trimmed.replace(/,/g, ''))
  return Number.isFinite(value) && value > 0 ? value : null
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
    specialty: first(query.specialty),
    county: first(query.county),
    group: first(query.group),
    sort: (SORTS as readonly string[]).includes(sort ?? '') ? (sort as SortKey) : 'rate',
    page: positiveInteger(first(query.page)) ?? 1,
    pageSize: positiveInteger(first(query.pageSize), 100) ?? DEFAULT_PAGE_SIZE,
    snapshot: positiveInteger(first(query.snapshot)),
    lens: lensSchema.safeParse(first(query.lens)).data,
    plan: first(query.plan),
    cases: positiveInteger(first(query.cases), MAX_CASES),
    dose: parseDose(first(query.dose)) ?? undefined,
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
  // Cases a year and a dose belong to one service: a different code starts without them (unless the patch sets them).
  const code = typeof patch.code === 'string' ? patch.code.trim() : ''
  const otherCode = code && code.toUpperCase() !== (first(current.code) ?? '').toUpperCase()
  if (otherCode && !('cases' in patch)) delete next.cases
  if (otherCode && !('dose' in patch)) delete next.dose
  if (next.sort === 'rate') delete next.sort
  if (next.page === '1') delete next.page
  if (next.pageSize === String(DEFAULT_PAGE_SIZE)) delete next.pageSize
  return next
}

/** Only what the API understands; lens, cases and dose are interpretation and never reach it. */
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
    specialty: params.specialty,
    county: params.county,
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
    .min(1, 'Type a service, like colonoscopy or 45378.')
    .max(8, 'Pick a service from the list.')
    .regex(/^[A-Za-z0-9]+$/, 'Pick a service from the list.'),
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
  specialty: z
    .string()
    .trim()
    .refine((value) => value === '' || value.length >= 2, { error: "Type at least 2 characters of a specialty, for example 'gastro'." })
    .refine((value) => value.length <= 100, { error: 'A specialty is at most 100 characters.' }),
  county: z
    .string()
    .trim()
    .refine((value) => value === '' || value.length >= 2, {
      error: "Enter a New York county or NYC borough, for example 'Brooklyn' or 'Monroe'.",
    })
    .refine((value) => value.length <= 60, { error: 'A location is at most 60 characters.' }),
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

/** What the provider role's box says when the text is neither an NPI nor a tax ID. */
export const PROVIDER_ID_ERROR = 'Enter a 10-digit NPI or a 9-digit tax ID.'

/**
 * The provider role's one box, read with the form's own rules: 10 digits is an NPI, 9 digits (dash optional) a tax ID.
 * Returns the search to run (the other one cleared, so they never disagree), or null when it's neither.
 */
export function providerIdPatch(text: string | null | undefined): QueryPatch | null {
  const value = (text ?? '').trim()
  if (!value) return null
  if (searchFormSchema.shape.npi.safeParse(value).success) return { npi: npiDigits(value), ein: null }
  if (searchFormSchema.shape.ein.safeParse(value).success) return { ein: normalizeEin(value) ?? value, npi: null }
  return null
}

export function formValuesFrom(params: SearchParams): SearchFormValues {
  return {
    code: params.code,
    npi: params.npi ?? '',
    ein: params.ein ?? '',
    provider: params.provider ?? '',
    specialty: params.specialty ?? '',
    county: params.county ?? '',
  }
}
