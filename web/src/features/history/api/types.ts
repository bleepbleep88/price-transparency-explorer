// Types mirroring the API's history records (src/PriceExplorer.Api/Core/Models/History). JSON is camelCase.

import type { ModifierRef, StatsLevel } from '@/features/rates/api/types'

export type ChangeKind = 'changed' | 'new' | 'dropped'

export interface SnapshotRef {
  snapshotId: number
  indexDate: string | null
  isLatest: boolean
}

export interface HistorySnapshot extends SnapshotRef {
  statisticsBuiltAt: string | null
  /** False while the saved statistics are rebuilt or before the first build: the changes may be out of date. */
  statisticsCurrent: boolean
}

export interface ChangesEcho {
  from: number
  to: number
  product: string | null
  code: string | null
  codeType: string | null
  kind: ChangeKind | null
  minChangePct: number | null
  listedElsewhere: boolean | null
  sort: string
  page: number
  pageSize: number
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
  /** Of the new ones: the older file lists the provider for the same code under other modifiers or another setting. */
  newListedElsewhere: number
  dropped: number
  droppedListedElsewhere: number
  unchanged: number
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
  /** Rates the provider has in the group on each side; with several, its value can move when one is added or removed. */
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
  /** Both snapshots' saved statistics are current. */
  current: boolean
  products: ChangesProduct[]
  counts: ChangeCounts
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
  /** The product's file in this snapshot looks partial. */
  partial: boolean
  providerCount: number
  statsLevel: StatsLevel
  min: number
  p25: number | null
  median: number | null
  p75: number | null
  max: number
  /** The searched provider's rate here, as the card marks it; null without a provider filter or when it has none here. */
  provider: TrendProvider | null
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
