import { describe, expect, it } from 'vitest'
import type { CodeInfo, ProviderPosition, RateGroup, RateSearchResult } from '@/features/rates/api/types'
import { codeFilter, compareLink, gapsLink, scorecardLink, searchLocation } from '../insightLinks'

function group(overrides: Partial<RateGroup> = {}): RateGroup {
  return {
    key: 'ambetter|CPT|45378|facility|asc||negotiated',
    productKey: 'ambetter',
    productLabel: 'Ambetter from Fidelis Care',
    codeType: 'CPT',
    code: '45378',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'asc',
    settingLabel: 'Ambulatory surgery center',
    settingNote: null,
    places: [],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per procedure',
    perUnit: false,
    billedAs: [],
    providerCount: 31,
    rateCount: 31,
    statsLevel: 'full',
    stats: { min: 226.51, p10: 371.78, p25: 454.45, median: 578.31, p75: 627.5, p90: 663.64, max: 1154.16, spreadMultiple: 5.1, p90ToP10: 1.79 },
    lowerQuartileGap: 123.86,
    listedRates: [],
    matchedProviders: 0,
    highlights: [],
    notes: [],
    ...overrides,
  }
}

const code: CodeInfo = {
  codeType: 'CPT',
  code: '45378',
  name: 'Colonoscopy',
  description: null,
  descriptionSource: 'payer_file',
  category: { key: 'surgery', label: 'Surgery & procedures', unit: 'per procedure', perUnit: false },
  levelFamily: null,
  foundVia: [],
}

function result(groups: RateGroup[], query: Partial<RateSearchResult['query']> = {}, latest = true): RateSearchResult {
  return {
    query: {
      code: '45378',
      matchedCodes: ['45378'],
      codeType: null,
      npi: null,
      ein: null,
      product: null,
      setting: null,
      feeType: null,
      provider: null,
      group: null,
      sort: 'rate',
      page: 1,
      pageSize: 25,
      ...query,
    },
    snapshot: { snapshotId: 3, indexDate: '2026-08-31', isLatest: latest },
    code,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

function position(providerKey: string, displayName = 'Clinic A'): ProviderPosition {
  return { providerKey, displayName, rate: 500, percentile: 40, vsMedian: 0.9, aboveP25: 10, rateLow: 500, rateHigh: 500 }
}

describe('codeFilter', () => {
  it('names the code with its type, so a short number stays the same code', () => {
    expect(codeFilter(result([group()]))).toBe('CPT 45378')
    expect(codeFilter({ ...result([group()]), code: null, query: { ...result([]).query, code: '0490' } })).toBe('0490')
  })
})

describe('gapsLink', () => {
  it("opens the leaderboard on this code under the search's filters when a group is ranked there", () => {
    expect(gapsLink(result([group()], { product: 'ambetter', setting: 'asc', feeType: 'facility', npi: '1234567893' }), 'spread')).toEqual({
      name: 'gaps',
      query: { code: 'CPT 45378', product: 'ambetter', setting: 'asc', feeType: 'facility', sort: 'spread' },
    })
  })

  it('opens the whole leaderboard when no group has 10 providers', () => {
    expect(gapsLink(result([group({ statsLevel: 'basic' })]), 'gap')).toEqual({ name: 'gaps', query: {} })
  })

  it('asks for facility charges when the code searched is a revenue code on its own', () => {
    const rc = group({ key: 'ambetter|RC|0360|facility|hospital-outpatient||negotiated', codeType: 'RC', code: '0360' })
    const search = { ...result([rc]), code: { ...code, codeType: 'RC', code: '0360' } }
    expect(gapsLink(search, 'gap')).toEqual({ name: 'gaps', query: { code: 'RC 0360', includeFacilityCharges: 'true' } })
  })

  it('keeps an earlier snapshot', () => {
    expect(gapsLink(result([group()], {}, false), 'gap')).toEqual({ name: 'gaps', query: { snapshot: '3', code: 'CPT 45378' } })
  })
})

describe('compareLink', () => {
  it('appears when two products have the same group for the code', () => {
    const essential = group({ key: 'essential|CPT|45378|facility|asc||negotiated', productKey: 'essential', productLabel: 'Essential Plan' })
    expect(compareLink(result([group(), essential], { setting: 'asc' }))).toEqual({ name: 'compare', query: { code: 'CPT 45378', setting: 'asc' } })
  })

  it('stays away when the groups differ in more than the product', () => {
    const office = group({ key: 'essential|CPT|45378|professional|office||negotiated', productKey: 'essential', feeType: 'professional', settingKey: 'office' })
    expect(compareLink(result([group(), office]))).toBeNull()
    expect(compareLink(result([group()]))).toBeNull()
  })
})

describe('scorecardLink', () => {
  it('links the one provider an EIN picked out', () => {
    const groups = [group({ matchedProviders: 1, highlights: [position('113471596', 'Minkowitz Consulting Pathologists PC')] }), group({ key: 'b' })]
    expect(scorecardLink(result(groups, { ein: '11-3471596' }))).toEqual({
      to: { name: 'scorecard', params: { key: '113471596' }, query: {} },
      name: 'Minkowitz Consulting Pathologists PC',
      byNpi: false,
    })
  })

  it('says when an NPI picked the provider, since the scorecard covers its whole tax ID', () => {
    const groups = [group({ matchedProviders: 1, highlights: [position('113471596')] })]
    expect(scorecardLink(result(groups, { npi: '1396747861' }))?.byNpi).toBe(true)
  })

  it('stays away for several providers, a name filter, or more matches than listed', () => {
    const two = [group({ matchedProviders: 1, highlights: [position('1')] }), group({ key: 'b', matchedProviders: 1, highlights: [position('2')] })]
    expect(scorecardLink(result(two, { npi: '1396747861' }))).toBeNull()
    expect(scorecardLink(result([group({ matchedProviders: 1, highlights: [position('1')] })], { provider: 'clinic' }))).toBeNull()
    expect(scorecardLink(result([group({ matchedProviders: 6, highlights: [position('1')] })], { ein: '11-3471596' }))).toBeNull()
  })
})

describe('searchLocation', () => {
  it("opens the search with the insight's own query", () => {
    expect(searchLocation({ code: '45378', codeType: 'CPT', group: 'g', ein: '113471596' })).toEqual({
      name: 'search',
      query: { code: '45378', codeType: 'CPT', group: 'g', ein: '113471596' },
    })
  })
})
