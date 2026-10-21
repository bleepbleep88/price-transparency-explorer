// Types mirroring the API's history records (src/PriceExplorer.Api/Core/Models/History). JSON is camelCase.

import type { ModifierRef, StatsLevel } from '@/features/rates/api/types'

export type ChangeKind = 'changed' | 'new' | 'dropped'
/** What the kind filter takes: a kind, or changed rates by direction. */
export type ChangeFilter = ChangeKind | 'increased' | 'decreased'

export interface SnapshotRef {
  snapshotId: number
  indexDate: string | null
  isLatest: boolean
}

export interface HistorySnapshot extends SnapshotRef {
  statisticsBuiltAt: string | null
  /** False while the saved statistics are rebuilt or before the first build: then there are no changes to show. */
  statisticsCurrent: boolean
}

export interface ChangesEcho {
  from: number
  to: number
  product: string | null
  code: string | null
  codeType: string | null
  kind: ChangeFilter | null
  minChangePct: number | null
  sort: string
  page: number
  pageSize: number
  includeFacilityCharges: boolean
  includeListedElsewhere: boolean
}

export interface ChangesProduct {
  productKey: string
  productLabel: string
  compared: boolean
  notComparedBecause: string | null
}

export interface ChangeCounts {
  changed: number
  increased: number
  decreased: number
  new: number
  /**
   * New rates the older file lists for the same provider and code under other modifiers or another setting: the same rate
   * relabeled. What includeListedElsewhere adds to `new` (already in it when set).
   */
  newListedElsewhere: number
  dropped: number
  /** Gone rates the newer file lists relabeled: what includeListedElsewhere adds to `dropped`. */
  droppedListedElsewhere: number
  unchanged: number
  /**
   * Facility charges with no procedure named (filed under a revenue code alone) that match every other filter, kind included: what
   * includeFacilityCharges adds. They don't compare like for like, so they're left out by default (decision 89).
   */
  facilityCharges: number
}

export interface RateChange {
  kind: ChangeKind
  groupKey: string
  productKey: string
  productLabel: string
  codeType: string
  code: string
  codeName: string | null
  feeType: string
  feeTypeLabel: string
  settingKey: string
  settingLabel: string
  modifiers: ModifierRef[]
  negotiatedType: string
  unit: string
  providerKey: string
  ein: string | null
  einDisplay: string
  einMasked: boolean
  businessName: string
  /** The provider's rate in the group in the older file (its search row's rate); null when new. */
  from: number | null
  /** In the newer file; null when dropped. */
  to: number | null
  change: number | null
  /** Rounded to 0.1; null unless changed from an amount above zero. */
  changePct: number | null
  fromRateId: number | null
  toRateId: number | null
  /** Rates the provider has in the group on each side. */
  fromRates: number | null
  toRates: number | null
  /** New or dropped, but the other file lists the provider for the same code under other modifiers or another setting. */
  listedElsewhere: boolean
}

export interface RateChangesPage {
  total: number
  page: number
  pageSize: number
  items: RateChange[]
}

export interface ChangesResult {
  query: ChangesEcho
  from: HistorySnapshot
  to: HistorySnapshot
  /** Both snapshots' saved statistics are built and current. Until then there are no counts and no rows. */
  current: boolean
  /** Why there are no counts or rows yet; null when current. */
  message: string | null
  products: ChangesProduct[]
  /** Null until current. */
  counts: ChangeCounts | null
  rows: RateChangesPage
}

export interface TrendProvider {
  providerKey: string
  displayName: string
  rate: number
}

/** A comparison group in one snapshot, as that snapshot's search card shows it. */
export interface TrendPoint {
  snapshotId: number
  indexDate: string | null
  /** The product's file in this snapshot looks incomplete next to nearby months. */
  partial: boolean
  providerCount: number
  statsLevel: StatsLevel
  min: number
  /** 10+ providers only: the band "most providers" get (10th to 90th percentile). */
  p10: number | null
  p25: number | null
  /** The typical price (the middle provider's), from 5 providers. */
  median: number | null
  p75: number | null
  p90: number | null
  max: number
  /** The searched provider's rate here, as the card marks it; null without a provider filter or when it has none here. */
  provider: TrendProvider | null
  /** How the providers behind the group moved since the previous complete file; null for the first and for incomplete ones. */
  sincePrevious: TrendStep | null
}

/**
 * Providers in both files, how many of those the file lists at a different, higher or lower rate, how many kept the same
 * rates with their clinicians moved between them (reweighted), and how many left or joined.
 */
export interface TrendStep {
  sinceSnapshotId: number
  stayed: number
  raised: number
  lowered: number
  reweighted: number
  left: number
  joined: number
}

export interface GroupTrend {
  key: string
  productKey: string
  /** One per snapshot where the group has rates, oldest first. */
  points: TrendPoint[]
}

export interface RateTrendResult {
  selected: SnapshotRef
  snapshots: SnapshotRef[]
  groups: GroupTrend[]
}
