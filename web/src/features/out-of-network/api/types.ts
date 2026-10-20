// Types mirroring the API's out-of-network records (src/PriceExplorer.Api/Core/Models/OutOfNetwork).

import type { ModifierRef, PlaceRef } from '@/features/rates/api/types'

/** Under 5 amounts they're listed (median null); from 5 on, min, median and max (listed empty). */
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
  /** In-network rates for this code are per unit; these amounts are for the whole service. */
  perUnitCode: boolean
  /** Unique payments: distinct allowed amounts per tax ID and place. */
  payments: number
  /** Tax IDs paid. */
  providers: number
  npis: number
  allowed: AmountSummary
  billed: AmountSummary | null
  /** Median of allowed ÷ billed (0.21 = 21% of the charge), from 5 pairs; null under 5. */
  allowedShareOfBilled: number | null
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
