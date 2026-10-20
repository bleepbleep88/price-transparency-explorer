import type { OutOfNetworkGroup, OutOfNetworkResult } from '../../api/types'

// Shaped like the real 2026-08-31 answers for revenue code 0450 (ER facility) and CPT 74177 (CT read).
export function oonGroup(overrides: Partial<OutOfNetworkGroup> = {}): OutOfNetworkGroup {
  return {
    key: 'essential-plan|RC|0450|facility|',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'RC',
    code: '0450',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    places: [{ pos: '23', name: 'Emergency room (hospital)' }],
    modifiers: [],
    perUnitCode: false,
    payments: 20,
    providers: 5,
    npis: 7,
    allowed: { count: 20, min: 60.12, median: 1375.18, max: 187538.55, listed: [] },
    billed: { count: 22, min: 551.92, median: 5110, max: 1733257, listed: [] },
    allowedShareOfBilled: 0.21,
    ...overrides,
  }
}

export const LISTED_CT_READ = oonGroup({
  key: 'ambetter-from-fidelis-care|CPT|74177|professional|',
  productKey: 'ambetter-from-fidelis-care',
  productLabel: 'Ambetter from Fidelis Care',
  codeType: 'CPT',
  code: '74177',
  feeType: 'professional',
  feeTypeLabel: 'Professional (clinician) fee',
  payments: 4,
  providers: 1,
  npis: 1,
  allowed: { count: 4, min: 217.92, median: null, max: 8373.66, listed: [217.92, 544.8, 762.72, 8373.66] },
  billed: { count: 4, min: 3892, median: null, max: 134647.88, listed: [3892, 9730, 13622, 134647.88] },
  allowedShareOfBilled: null,
})

export function oonResult(groups: OutOfNetworkGroup[]): OutOfNetworkResult {
  return {
    query: { code: '0450', matchedCodes: ['0450', '00450'], codeType: null },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    files: [],
    groups,
  }
}
