import { settingName } from '@/lib/plainNames'
import type { LocationOption, ProductFileSummary, RateSearchResult, SettingOption } from '../api/types'
import { locationTitle } from './locations'
import type { SearchParams } from './searchParams'

export interface FilterChip {
  key: keyof SearchParams
  label: string
  /** The chip names the provider being looked up: it carries the copper mark. */
  yours?: boolean
}

/** Removable chips for every filter that narrows the results, in plain words. */
export function activeFilterChips(
  result: RateSearchResult,
  params: SearchParams,
  products: ProductFileSummary[],
  settings: SettingOption[],
  locations: LocationOption[],
): FilterChip[] {
  const chips: FilterChip[] = []
  if (params.codeType) chips.push({ key: 'codeType', label: `Code type: ${params.codeType}` })
  if (params.npi) chips.push({ key: 'npi', label: `NPI ${params.npi}`, yours: true })
  if (params.ein) chips.push({ key: 'ein', label: params.ein.startsWith('u') ? 'One provider' : `Tax ID ${params.ein}`, yours: true })
  if (params.provider) chips.push({ key: 'provider', label: `Name: ${params.provider}`, yours: true })
  if (params.specialty) chips.push({ key: 'specialty', label: params.specialty })
  if (params.county) {
    // The place the API matched ("Brooklyn" for "Kings County"), else the list's name for it.
    const matched = result.query.county === params.county ? result.query.countyPlace : null
    chips.push({ key: 'county', label: matched ?? locationTitle(params.county, locations) })
  }
  if (params.product) chips.push({ key: 'product', label: products.find((x) => x.productKey === params.product)?.productLabel ?? params.product })
  if (params.setting) {
    const label = settings.find((x) => x.key === params.setting)?.label
    chips.push({ key: 'setting', label: settingName(params.setting, label) })
  }
  if (params.feeType) chips.push({ key: 'feeType', label: params.feeType === 'facility' ? "The place's bill" : "The doctor's bill" })
  return chips
}
