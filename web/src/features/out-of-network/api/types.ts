// Types mirroring the API's out-of-network records (src/PriceExplorer.Api/Core/Models/OutOfNetwork).

import type { ModifierRef, PlaceRef } from '@/features/rates/api/types'

/** From fewer than 5 providers the amounts are listed (median null); from 5 providers on, min, median and max (listed empty). */
export interface AmountSummary {
  count: number
  min: number
  median: number | null
  max: number
  listed: number[]
}

export interface OutOfNetworkGroup {
  key: string
  productKey: string
  productLabel: string
  codeType: string
  code: string
  feeType: 'facility' | 'professional' | string
  feeTypeLabel: string
  places: PlaceRef[]
  modifiers: ModifierRef[]
  /** In-network rates for this code are per unit; the files don't say how many units an out-of-network amount covers. */
  perUnitCode: boolean
  /** Different amounts listed: distinct allowed amounts per tax ID and place. Not a claim count; the files have none. */
  payments: number
  /** Tax IDs paid. */
  providers: number
  npis: number
  allowed: AmountSummary
  billed: AmountSummary | null
  /** Median of billed ÷ allowed (4.81: providers usually billed about 4.8 times what the plan allowed), from 5 providers; null with fewer. */
  billedToAllowed: number | null
}

export interface OutOfNetworkFile {
  fileId: number
  productKey: string
  productLabel: string
  url: string
  status: string
  fileLastUpdatedOn: string | null
}

export interface OutOfNetworkResult {
  query: { code: string; matchedCodes: string[]; codeType: string | null }
  snapshot: { snapshotId: number; indexDate: string | null; isLatest: boolean }
  files: OutOfNetworkFile[]
  groups: OutOfNetworkGroup[]
}
