// Types mirroring the API's C# records (src/PriceExplorer.Api/Core/Models). JSON is camelCase; dates are ISO strings.

export type IngestionPhase = 'starting' | 'downloading' | 'parsing' | 'saving' | 'ready' | 'failed'

export interface IngestionIssue {
  severity: 'info' | 'warning' | 'error' | string
  message: string
  source: string | null
}

export interface IngestionStatus {
  phase: IngestionPhase
  message: string
  dataAvailable: boolean
  currentFile: string | null
  currentFileProgress: number | null
  filesDone: number
  filesTotal: number
  issues: IngestionIssue[]
  updatedAt: string
  version: number
}

export interface ProductFileSummary {
  fileId: number
  productKey: string
  productLabel: string
  url: string
  status: 'pending' | 'loaded' | 'partial' | 'failed' | string
  error: string | null
  warning: string | null
  fileLastUpdatedOn: string | null
  httpLastModified: string | null
  contentLength: number | null
  sha256: string | null
  planEntries: number
  distinctPlanIds: number
  items: number | null
  pricesPublished: number | null
  uniqueRates: number | null
  duplicatesRemoved: number | null
  distinctNpis: number | null
  distinctEins: number | null
  loadedAt: string | null
}

export interface SnapshotSummary {
  snapshotId: number
  indexDate: string | null
  indexUrl: string
  indexLastUpdatedOn: string | null
  reportingEntityName: string | null
  reportingEntityType: string | null
  status: 'loading' | 'ready' | 'partial' | 'failed' | string
  isLatest: boolean
  products: ProductFileSummary[]
}

export interface PlanSummary {
  planId: string | null
  planIdType: string | null
  marketType: string | null
  issuerName: string | null
  sponsorName: string | null
  names: string[]
}

export interface ProductPlans {
  productKey: string
  productLabel: string
  planEntries: number
  plans: PlanSummary[]
}

export interface SnapshotPlans {
  snapshotId: number
  products: ProductPlans[]
}

export interface ServiceCategory {
  key: string
  label: string
  unit: string
  perUnit: boolean
}

export interface CodeInfo {
  codeType: string
  code: string
  name: string | null
  description: string | null
  descriptionSource: 'payer_file' | 'none' | string
  category: ServiceCategory
  levelFamily: string | null
  foundVia: string[]
}

export interface RateStats {
  min: number
  p10: number | null
  p25: number | null
  median: number | null
  p75: number | null
  p90: number | null
  max: number
  spreadMultiple: number | null
  p90ToP10: number | null
}

export interface ProviderPosition {
  providerKey: string
  displayName: string
  rate: number
  percentile: number | null
  vsMedian: number | null
  aboveP25: number | null
}

export interface PlaceRef {
  pos: string
  name: string
}

export interface ModifierRef {
  code: string
  label: string
}

export interface BilledAs {
  billingCodeType: string
  billingCode: string
  label: string
}

export type StatsLevel = 'list' | 'basic' | 'full'

export interface RateGroup {
  key: string
  productKey: string
  productLabel: string
  codeType: string
  code: string
  feeType: 'facility' | 'professional' | string
  feeTypeLabel: string
  settingKey: string
  settingLabel: string
  settingNote: string | null
  places: PlaceRef[]
  modifiers: ModifierRef[]
  negotiatedType: string
  unit: string
  perUnit: boolean
  billedAs: BilledAs[]
  providerCount: number
  rateCount: number
  statsLevel: StatsLevel
  stats: RateStats
  savingsPerCase: number | null
  listedRates: number[]
  highlights: ProviderPosition[]
  notes: string[]
}

export interface RateRow {
  groupKey: string
  providerKey: string
  ein: string | null
  einDisplay: string
  einMasked: boolean
  einLevel: boolean
  businessName: string
  otherNames: string[]
  rate: number
  rateLow: number
  rateHigh: number
  multipleRates: boolean
  vsMedian: number | null
  percentile: number | null
  band: 'low' | 'typical' | 'high' | null
  npiCount: number
  occurrences: number
  rateCount: number
  firstRateId: number
  matched: boolean
}

export interface RateRowsPage {
  total: number
  page: number
  pageSize: number
  items: RateRow[]
}

export interface HintLink {
  label: string
  query: Record<string, string | null>
}

export interface SearchHint {
  kind: string
  severity: 'info' | 'warning'
  message: string
  links: HintLink[]
}

export interface SearchQuality {
  publishedRows: number
  uniqueRates: number
  duplicatesCollapsed: number
  providersWithMultipleRates: number
  maskedTaxIds: number
  wholeTaxIdContracts: number
}

export interface RateSearchEcho {
  code: string
  matchedCodes: string[]
  codeType: string | null
  npi: string | null
  ein: string | null
  product: string | null
  setting: string | null
  feeType: string | null
  provider: string | null
  group: string | null
  sort: string
  page: number
  pageSize: number
}

export interface RateSearchResult {
  query: RateSearchEcho
  snapshot: { snapshotId: number; indexDate: string | null; isLatest: boolean }
  code: CodeInfo | null
  groups: RateGroup[]
  rows: RateRowsPage
  hints: SearchHint[]
  quality: SearchQuality
}

export interface RateSourceProviderGroup {
  providerGroupId: number
  groupIndex: number
  taxIdDisplay: string
  taxIdMasked: boolean
  businessName: string | null
  npiCount: number
  wholeTaxId: boolean
  networkNames: string | null
}

export interface RateSource {
  rateId: number
  file: {
    fileId: number
    productKey: string
    productLabel: string
    url: string
    sha256: string | null
    lastUpdatedOn: string | null
    httpLastModified: string | null
    contentLength: number | null
    reportingEntityName: string | null
    schemaVersion: string | null
  }
  snapshot: { snapshotId: number; indexUrl: string; indexDate: string | null; indexLastUpdatedOn: string | null }
  jsonPath: string
  billingCodeType: string
  billingCode: string
  billingCodePublished: string
  codeType: string
  code: string
  codeSource: 'billing_code' | 'additional_information' | string
  additionalInformation: string | null
  negotiationArrangement: string
  negotiatedType: string
  rate: number
  expirationDate: string | null
  billingClass: string
  setting: string
  places: PlaceRef[]
  modifiers: ModifierRef[]
  occurrences: number
  itemLabelCount: number
  itemLabelSamples: string[]
  providerGroups: RateSourceProviderGroup[]
}

export interface ListedNpi {
  npi: number
  valid: boolean
}

export interface RateNpisPage {
  rateId: number
  providerKey: string | null
  total: number
  page: number
  pageSize: number
  items: ListedNpi[]
}

export interface CodeSuggestion {
  codeType: string
  code: string
  name: string | null
  description: string | null
  category: string
  unit: string
  billedDirectly: boolean
  facilityRevenueCodes: string[]
  rates: number
}

export interface ProviderSuggestion {
  providerKey: string
  taxIdDisplay: string
  taxIdMasked: boolean
  businessName: string
  otherNames: string[]
  rates: number
}

export interface SettingOption {
  key: string
  label: string
  note: string | null
}

export interface ReferenceLists {
  settings: SettingOption[]
  sorts: string[]
}
