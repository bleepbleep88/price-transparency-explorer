import type { ProductFileSummary, SnapshotSummary } from '@/features/rates/api/types'
import type { GroupTrend, RateChange, TrendPoint } from '../../api/types'

// Shapes the history tests share, close to what the API returns for the three Fidelis snapshots.

export function product(overrides: Partial<ProductFileSummary> = {}): ProductFileSummary {
  return {
    fileId: 1,
    productKey: 'ambetter-from-fidelis-care',
    productLabel: 'Ambetter from Fidelis Care',
    url: 'https://example.test/file.json',
    status: 'loaded',
    error: null,
    warning: null,
    fileLastUpdatedOn: null,
    httpLastModified: null,
    contentLength: null,
    sha256: null,
    planEntries: 88,
    distinctPlanIds: 23,
    items: 1500,
    pricesPublished: 180_000,
    uniqueRates: 7_700,
    duplicatesRemoved: 172_000,
    distinctNpis: 43_000,
    distinctEins: 5_700,
    loadedAt: null,
    completeness: { score: 1, rates: 1, eins: 1, codes: 1, comparedWith: [2, 3], partial: false },
    ...overrides,
  }
}

export function snapshot(id: number, indexDate: string, overrides: Partial<SnapshotSummary> = {}): SnapshotSummary {
  return {
    snapshotId: id,
    indexDate,
    indexUrl: `https://example.test/${indexDate}_fidelis_index.json`,
    indexLastUpdatedOn: indexDate,
    reportingEntityName: 'Centene Management Company LLC',
    reportingEntityType: 'Third Party Administrator',
    status: 'ready',
    isLatest: false,
    products: [product()],
    ...overrides,
  }
}

/** Ids are given out in load order, newest first, as on a first start. */
export const SNAPSHOTS: SnapshotSummary[] = [
  snapshot(1, '2026-09-28', { isLatest: true }),
  snapshot(2, '2026-08-31'),
  snapshot(3, '2026-07-27'),
]

export function point(snapshotId: number, indexDate: string, overrides: Partial<TrendPoint> = {}): TrendPoint {
  return {
    snapshotId,
    indexDate,
    partial: false,
    providerCount: 31,
    statsLevel: 'full',
    min: 226.51,
    p10: 371.78,
    p25: 433.08,
    median: 578.31,
    p75: 625,
    p90: 663.64,
    max: 1154.16,
    provider: null,
    sincePrevious: null,
    ...overrides,
  }
}

export function trend(points: TrendPoint[], key = 'ambetter|CPT|45378|facility|asc||negotiated'): GroupTrend {
  return { key, productKey: 'ambetter', points }
}

export function change(overrides: Partial<RateChange> = {}): RateChange {
  return {
    kind: 'changed',
    groupKey: 'ambetter|CPT|45378|facility|asc||negotiated',
    productKey: 'ambetter',
    productLabel: 'Ambetter from Fidelis Care',
    codeType: 'CPT',
    code: '45378',
    codeName: 'Lower GI endoscopy',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'asc',
    settingLabel: 'Ambulatory surgery center',
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per procedure',
    providerKey: '221111111',
    ein: '221111111',
    einDisplay: '22-1111111',
    einMasked: false,
    businessName: 'Alpha Surgery Center',
    from: 600,
    to: 660,
    change: 60,
    changePct: 10,
    fromRateId: 11,
    toRateId: 22,
    fromRates: 1,
    toRates: 1,
    listedElsewhere: false,
    ...overrides,
  }
}
