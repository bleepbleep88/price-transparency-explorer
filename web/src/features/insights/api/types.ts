// Types mirroring the insights DTOs (src/PriceExplorer.Api/Core/Models/Insights/InsightDtos.cs). JSON is camelCase.

import type { ModifierRef, RateStats } from '@/features/rates/api/types'

/** Query parameters for the search page (and GET /api/v1/rates) that open one comparison group. */
export type SearchLink = Record<string, string>

export interface StatisticsBasis {
  snapshot: { snapshotId: number; indexDate: string | null; isLatest: boolean }
  /** When the snapshot's statistics were built; null until the first build finishes. */
  builtAt: string | null
  /**
   * False until the statistics are first built, and whenever they no longer match search: a rate file was saved again, a
   * different build of the app built them, or a rate in them expired. The saved numbers still answer meanwhile.
   */
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

export type GapSort = 'gap' | 'spread' | 'range'

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
    includeFacilityCharges: boolean
    sort: GapSort
    page: number
    pageSize: number
  }
  minimumProviders: number
  categories: CategoryOption[]
  /** Groups of charges filed under a revenue code with no procedure named; ranked only with includeFacilityCharges. */
  facilityCharges: number
  /** Groups where the lower-priced quarter get the typical price: never ranked. */
  noDifference: number
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
  /** Filed under a revenue code with no procedure named: left out of the overall reading unless included. */
  facilityCharge: boolean
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
  includeFacilityCharges: boolean
  /** Its facility charges with a typical price: what including them adds to the overall reading and the lists. */
  facilityCharges: number
  notes: string[]
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
  /** The two rates are exactly equal (a ratio that rounds to 1.00 can still differ). */
  same: boolean
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
    includeFacilityCharges: boolean
    sort: CompareSort
    page: number
    pageSize: number
  }
  product: ProductRef
  versus: ProductRef
  products: ProductRef[]
  categories: CategoryOption[]
  facilityCharges: number
  /** Middle points need 5 distinct providers and the 10th/90th need 10; otherwise null. */
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
  byCategory: { key: string; label: string; pairs: number; providers: number; identical: number; lower: number; higher: number; median: number | null }[]
  pairs: { total: number; page: number; pageSize: number; items: ComparisonPair[] }
  notes: string[]
}
