// Types mirroring the insights DTOs (src/PriceExplorer.Api/Core/Models/Insights/InsightDtos.cs). JSON is camelCase.

import type { ModifierRef, RateStats } from '@/features/rates/api/types'

/** Query parameters for the search page (and GET /api/v1/rates) that open one comparison group. */
export type SearchLink = Record<string, string>

export interface StatisticsBasis {
  snapshot: { snapshotId: number; indexDate: string | null; isLatest: boolean }
  /** When the snapshot's statistics were built; null until the first build finishes. */
  builtAt: string | null
  /** False while they're missing or being rebuilt (a reload, or a rate expired since): numbers may not match a search yet. */
  isCurrent: boolean
}

export interface CategoryOption {
  key: string
  label: string
  count: number
}

export interface ProductRef {
  key: string
  label: string
}

export type GapSort = 'gap' | 'spread'

export interface GapRow {
  rank: number
  groupKey: string
  productKey: string
  productLabel: string
  codeType: string
  code: string
  codeName: string | null
  categoryKey: string
  categoryLabel: string
  feeType: string
  feeTypeLabel: string
  settingKey: string
  settingLabel: string
  modifiers: ModifierRef[]
  negotiatedType: string
  unit: string
  perUnit: boolean
  providerCount: number
  stats: RateStats
  lowerQuartileGap: number
  search: SearchLink
}

export interface GapLeaderboard {
  statistics: StatisticsBasis
  query: {
    product: string | null
    category: string | null
    setting: string | null
    feeType: string | null
    code: string | null
    matchedCodes: string[]
    sort: GapSort
    page: number
    pageSize: number
  }
  minimumProviders: number
  categories: CategoryOption[]
  rows: { total: number; page: number; pageSize: number; items: GapRow[] }
  notes: string[]
}

export type Band = 'low' | 'typical' | 'high'

export interface ScorecardGroup {
  groupKey: string
  productKey: string
  productLabel: string
  codeType: string
  code: string
  codeName: string | null
  categoryKey: string
  categoryLabel: string
  feeType: string
  feeTypeLabel: string
  settingKey: string
  settingLabel: string
  modifiers: ModifierRef[]
  negotiatedType: string
  unit: string
  perUnit: boolean
  providerCount: number
  statsLevel: 'list' | 'basic' | 'full'
  median: number | null
  value: number
  rateLow: number
  rateHigh: number
  percentile: number | null
  vsMedian: number | null
  band: Band | null
  search: SearchLink
}

export interface ProviderScorecard {
  statistics: StatisticsBasis
  provider: {
    providerKey: string
    ein: string | null
    einDisplay: string
    einMasked: boolean
    name: string
    otherNames: string[]
    npiCount: number
    wholeTaxIdContract: boolean
    products: { key: string; label: string; groups: number }[]
  }
  index: {
    value: number | null
    groupsCounted: number
    groupsTotal: number
    minimumGroups: number
    minimumProviders: number
    below: number
    atMedian: number
    above: number
  }
  highest: ScorecardGroup[]
  lowest: ScorecardGroup[]
  groups: ScorecardGroup[]
}

export type CompareSort = 'difference' | 'ratio' | '-ratio'

export interface RatioBand {
  key: string
  label: string
  direction: 'lower' | 'same' | 'higher'
  count: number
}

export interface ComparisonPair {
  providerKey: string
  ein: string | null
  einDisplay: string
  einMasked: boolean
  businessName: string
  codeType: string
  code: string
  codeName: string | null
  categoryKey: string
  categoryLabel: string
  feeType: string
  feeTypeLabel: string
  settingKey: string
  settingLabel: string
  modifiers: ModifierRef[]
  unit: string
  perUnit: boolean
  productValue: number
  versusValue: number
  /** productValue ÷ versusValue, rounded to 2 decimals. */
  ratio: number
  difference: number
  productSearch: SearchLink
  versusSearch: SearchLink
}

export interface ProductComparison {
  statistics: StatisticsBasis
  query: {
    code: string | null
    matchedCodes: string[]
    category: string | null
    setting: string | null
    sort: CompareSort
    page: number
    pageSize: number
  }
  product: ProductRef
  versus: ProductRef
  products: ProductRef[]
  categories: CategoryOption[]
  summary: {
    pairs: number
    providers: number
    codes: number
    identical: number
    lower: number
    higher: number
    p10: number | null
    p25: number | null
    median: number | null
    p75: number | null
    p90: number | null
  }
  distribution: RatioBand[]
  byCategory: { key: string; label: string; pairs: number; identical: number; lower: number; higher: number; median: number }[]
  pairs: { total: number; page: number; pageSize: number; items: ComparisonPair[] }
  notes: string[]
}
