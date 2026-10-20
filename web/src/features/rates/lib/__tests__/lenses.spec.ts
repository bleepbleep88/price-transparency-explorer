import { describe, expect, it } from 'vitest'
import type { CodeInfo, RateGroup, RateSearchResult } from '../../api/types'
import {
  apiSearchUrl,
  costLines,
  filedUnderRevenueCode,
  groupName,
  hasFacilityCharges,
  hasProviderFilter,
  isDrugCode,
  LENS_OPTIONS,
  qualityLines,
  spreadLines,
  spreadText,
  START_HERE,
  unitPriceLines,
  unitPriceText,
  widestSpread,
  yourRateLines,
} from '../lenses'
import { LENSES, parseRouteQuery } from '../searchParams'

// Shaped like the real 2026-08-31 responses the examples point at.
function makeGroup(overrides: Partial<RateGroup> = {}): RateGroup {
  return {
    key: 'ambetter|RC|0490|facility|hopd||negotiated',
    productKey: 'ambetter',
    productLabel: 'Ambetter from Fidelis Care',
    codeType: 'RC',
    code: '0490',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'hospital-outpatient',
    settingLabel: 'Hospital outpatient department',
    settingNote: null,
    places: [],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per facility charge',
    perUnit: false,
    billedAs: [],
    providerCount: 10,
    rateCount: 10,
    statsLevel: 'full',
    stats: { min: 512.4, p10: 958.31, p25: 1361.3, median: 2762.15, p75: 4200, p90: 5921.67, max: 9091.97, spreadMultiple: 17.74, p90ToP10: 6.18 },
    lowerQuartileGap: 1400.85,
    listedRates: [],
    highlights: [],
    notes: [],
    ...overrides,
  }
}

function makeResult(groups: RateGroup[], overrides: Partial<RateSearchResult> = {}): RateSearchResult {
  return {
    query: {
      code: '0490',
      matchedCodes: ['0490'],
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
    },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code: null,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 6642, uniqueRates: 28, duplicatesCollapsed: 6614, providersWithMultipleRates: 1, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
    ...overrides,
  }
}

const essentialBasic = makeGroup({
  key: 'essential|RC|0490|facility|hopd||negotiated',
  productLabel: 'Essential Plan',
  providerCount: 8,
  statsLevel: 'basic',
  stats: { min: 1200, p10: null, p25: null, median: 3595.92, p75: null, p90: null, max: 5768.22, spreadMultiple: 4.81, p90ToP10: null },
  lowerQuartileGap: null,
})
const singleEr = makeGroup({
  key: 'ambetter|RC|0490|facility|er||negotiated',
  settingLabel: 'Hospital emergency room',
  providerCount: 1,
  statsLevel: 'list',
  stats: { min: 4803.34, p10: null, p25: null, median: null, p75: null, p90: null, max: 4803.34, spreadMultiple: null, p90ToP10: null },
  lowerQuartileGap: null,
  listedRates: [4803.34],
})

describe('lens options and start cards', () => {
  it('covers every lens the URL accepts, each with one or two examples', () => {
    expect(LENS_OPTIONS.map((option) => option.key)).toEqual([...LENSES])
    expect(START_HERE.map((card) => card.key)).toEqual([...LENSES])
    for (const card of START_HERE) expect(card.examples.length).toBeGreaterThanOrEqual(1)
    for (const card of START_HERE) expect(card.examples.length).toBeLessThanOrEqual(2)
  })

  it("keeps the old examples: 45378, 29881, 99214, 812 and J0585", () => {
    const codes = START_HERE.flatMap((card) => card.examples.map((example) => example.code))
    expect(codes).toEqual(expect.arrayContaining(['45378', '29881', '99214', '812', 'J0585']))
  })

  it('names a group by product, fee type, setting and modifiers', () => {
    expect(groupName(makeGroup({ modifiers: [{ code: 'TC', label: 'Technical component' }] }))).toBe(
      'Ambetter from Fidelis Care · Facility fee · Hospital outpatient department (TC)',
    )
  })
})

describe('providers lens', () => {
  const office = makeGroup({
    key: 'ambetter|CPT|99213|professional|office|25|negotiated',
    feeTypeLabel: 'Professional (clinician) fee',
    settingLabel: "Doctor's office",
    providerCount: 124,
    highlights: [{ providerKey: '995084138', displayName: 'Heel Sole Podiatry PC', rate: 80.72, percentile: 68, vsMedian: 1.57, aboveP25: 29.27 }],
  })

  it('needs an NPI, EIN or name filter', () => {
    expect(hasProviderFilter(makeResult([office]))).toBe(false)
    const result = makeResult([office])
    expect(hasProviderFilter({ ...result, query: { ...result.query, npi: '1801474036' } })).toBe(true)
    expect(hasProviderFilter({ ...result, query: { ...result.query, provider: 'heel' } })).toBe(true)
  })

  it('says where your rate sits, by percentile and multiple of the median', () => {
    const [line] = yourRateLines(makeResult([office]))
    expect(line).toMatchObject({ provider: 'Heel Sole Podiatry PC', rate: 80.72 })
    expect(line!.text).toBe('Your rate $80.72 is at the 68th percentile, 1.57× the median.')
  })

  it('does not invent a percentile for a group too small to have one', () => {
    const small = { ...singleEr, highlights: [{ providerKey: 'x', displayName: 'Lone Hospital', rate: 4803.34, percentile: null, vsMedian: null, aboveP25: null }] }
    expect(yourRateLines(makeResult([small]))[0]!.text).toBe('Your rate $4,803.34. With 1 provider in this group there is no percentile.')
  })
})

describe('plans lens', () => {
  const lines = spreadLines(makeResult([makeGroup(), essentialBasic, singleEr]))

  it('measures spread for every group with statistics', () => {
    expect(lines.map((line) => line.providers)).toEqual([10, 8])
    expect(lines[0]!.highestToMedian).toBeCloseTo(9091.97 / 2762.15)
    expect(spreadText(lines[0]!)).toBe(
      'The 90th percentile is 6.18× the 10th; the highest rate is 3.29× the median; the median sits $1,400.85 above the lower quartile.',
    )
    expect(spreadText(lines[1]!)).toBe('The highest rate is 1.60× the median.')
  })

  it('ranks the widest spread first, preferring groups with full statistics', () => {
    const narrow = { ...lines[0]!, groupKey: 'narrow', p90ToP10: 1.43, providers: 100 }
    expect(widestSpread([narrow, lines[1]!, lines[0]!]).map((line) => line.groupKey)).toEqual([lines[0]!.groupKey, 'narrow'])
    expect(widestSpread([lines[1]!])).toEqual([lines[1]])
  })
})

describe('employers lens', () => {
  const asc = makeGroup({ key: 'asc', settingLabel: 'Ambulatory surgery center', providerCount: 31, lowerQuartileGap: 123.86, stats: { ...makeGroup().stats, median: 578.31 } })

  it('lists the typical cost and the gap per case, largest groups first', () => {
    const lines = costLines(makeResult([essentialBasic, asc, singleEr]))
    expect(lines.map((line) => [line.providers, line.median, line.gap])).toEqual([
      [31, 578.31, 123.86],
      [8, 3595.92, null],
    ])
    expect(lines.every((line) => line.yearly === null)).toBe(true)
  })

  it('turns cases a year into a yearly figure only where there is a gap', () => {
    const [first, second] = costLines(makeResult([asc, essentialBasic]), 1200)
    expect(first!.yearly).toBeCloseTo(123.86 * 1200)
    expect(second!.yearly).toBeNull()
  })
})

describe('innovators lens', () => {
  it('puts the clean-up numbers in plain words', () => {
    expect(qualityLines(makeResult([]).quality)).toEqual([
      '6,642 published rows became 28 unique rates.',
      '6,614 exact duplicates collapsed.',
      '0 tax IDs masked: prefixes the IRS never issues.',
      '0 whole-tax-ID contracts: no individual NPIs listed.',
    ])
  })

  it('builds the API request for this exact search, without lens or cases', () => {
    const params = parseRouteQuery({ code: '29881', lens: 'innovators', cases: '12', setting: 'asc' })
    expect(apiSearchUrl(params, 'http://localhost:5173')).toBe(
      'http://localhost:5173/api/v1/rates?code=29881&setting=asc&sort=rate&page=1&pageSize=25',
    )
  })
})

describe('life sciences lens', () => {
  const drug: CodeInfo = {
    codeType: 'HCPCS',
    code: 'J0585',
    name: 'BOTULINUM TOXIN TYPE A PER U',
    description: null,
    descriptionSource: 'payer_file',
    category: { key: 'drugs', label: 'Drugs & biologicals', unit: 'per HCPCS unit', perUnit: true },
    levelFamily: null,
    foundVia: [],
  }
  const unit = "per HCPCS unit (see the code's description)"
  const billedDirectly = [{ billingCodeType: 'HCPCS', billingCode: 'J0585', label: 'HCPCS J0585' }]
  const office = makeGroup({
    key: 'office-jw',
    codeType: 'HCPCS',
    code: 'J0585',
    productLabel: 'Essential Plan',
    feeTypeLabel: 'Professional (clinician) fee',
    settingLabel: "Doctor's office",
    modifiers: [{ code: 'JW', label: 'Drug amount discarded' }],
    unit,
    perUnit: true,
    billedAs: billedDirectly,
    providerCount: 7,
    statsLevel: 'basic',
    stats: { min: 5.1, p10: null, p25: null, median: 6.97, p75: null, p90: null, max: 8.2, spreadMultiple: 1.61, p90ToP10: null },
    lowerQuartileGap: null,
  })
  const small = {
    ...singleEr,
    key: 'small',
    codeType: 'HCPCS',
    code: 'J0585',
    feeTypeLabel: 'Professional (clinician) fee',
    settingLabel: "Doctor's office",
    unit,
    perUnit: true,
    billedAs: billedDirectly,
    providerCount: 4,
    listedRates: [7.16, 9.77, 8.46, 7.16],
  }
  // The surgery center's $551.29 is a facility charge filed under revenue code 0490, with J0585 only in the notes.
  const ascCharge = {
    ...singleEr,
    key: 'asc',
    codeType: 'HCPCS',
    code: 'J0585',
    productLabel: 'Essential Plan',
    settingLabel: 'Ambulatory surgery center',
    unit,
    perUnit: true,
    billedAs: [{ billingCodeType: 'RC', billingCode: '0490', label: 'Revenue code 0490 (Ambulatory surgery)' }],
    listedRates: [551.29],
  }

  it("goes by the API's category, not the code's letters", () => {
    expect(isDrugCode(drug)).toBe(true)
    expect(isDrugCode({ ...drug, code: 'Q0091', category: { ...drug.category, key: 'other' } })).toBe(false)
    expect(isDrugCode({ ...drug, codeType: 'CPT', code: '90715', category: { ...drug.category, key: 'drugs' } })).toBe(true)
    expect(isDrugCode(null)).toBe(false)
  })

  it('prices each site of care per unit: median, a single rate, or the listed range', () => {
    const lines = unitPriceLines(makeResult([office, small]))
    expect(lines.map((line) => [line.site, unitPriceText(line)])).toEqual([
      ["Doctor's office · Professional (clinician) fee", '$7.16 to $9.77'],
      ["Doctor's office · Professional (clinician) fee (JW)", '$6.97 median'],
    ])
  })

  it('leaves revenue-code facility charges and non-unit prices out of the per-unit table', () => {
    const notPerUnit = { ...office, key: 'fqhc', settingLabel: 'Federally qualified health center', perUnit: false, unit: 'per visit (all-inclusive)' }
    const result = makeResult([office, ascCharge, notPerUnit])
    expect(unitPriceLines(result).map((line) => line.groupKey)).toEqual(['office-jw'])
    expect(filedUnderRevenueCode(ascCharge)).toBe(true)
    expect(filedUnderRevenueCode(office)).toBe(false)
    expect(hasFacilityCharges(result)).toBe(true)
    expect(hasFacilityCharges(makeResult([office]))).toBe(false)
  })
})
