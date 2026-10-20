import { describe, expect, it } from 'vitest'
import type { CodeInfo, RateGroup, RateSearchResult } from '../../api/types'
import {
  apiSearchUrl,
  costLines,
  filedUnderRevenueCode,
  groupName,
  hasFacilityCharges,
  hasProviderFilter,
  highlightHeading,
  isDrugCode,
  modifierText,
  noYearlyReason,
  LENS_OPTIONS,
  qualityLines,
  spreadLines,
  spreadText,
  START_HERE,
  unitPriceLines,
  unitPriceText,
  widestSpread,
  yearlyLead,
  yearlyText,
  EMPLOYER_CAVEATS,
  yourRateGroups,
  yourRateLines,
} from '../lenses'
import { LENSES, parseRouteQuery } from '../searchParams'

const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|index|comparison group/i

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
    matchedProviders: 0,
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

  it('says what each lens answers without a statistics word', () => {
    const texts = [...LENS_OPTIONS.map((option) => option.answers), ...START_HERE.flatMap((card) => card.examples.map((example) => example.label))]
    for (const text of texts) expect(text).not.toMatch(BANNED)
  })

  it('names a group by product, fee, setting and modifiers, never a bare modifier number', () => {
    expect(groupName(makeGroup({ modifiers: [{ code: 'TC', label: 'Technical component' }] }))).toBe(
      'Ambetter from Fidelis Care · Facility fee · Hospital outpatient department · Technical component (modifier TC)',
    )
    expect(modifierText([{ code: 'Q1', label: 'Modifier Q1' }, { code: '33', label: 'Preventive service' }])).toBe(
      'modifier Q1, Preventive service (modifier 33)',
    )
    expect(groupName(makeGroup({ feeType: 'professional', feeTypeLabel: 'Professional (clinician) fee' }))).toBe(
      "Ambetter from Fidelis Care · Doctor's fee · Hospital outpatient department",
    )
  })
})

describe('providers lens', () => {
  const office = makeGroup({
    key: 'ambetter|CPT|99213|professional|office|25|negotiated',
    feeType: 'professional',
    feeTypeLabel: 'Professional (clinician) fee',
    settingLabel: "Doctor's office",
    providerCount: 124,
    stats: { min: 25.17, p10: 41.2, p25: 51.45, median: 51.45, p75: 77.5, p90: 95.3, max: 180.4, spreadMultiple: 7.17, p90ToP10: 2.31 },
    lowerQuartileGap: 0,
    highlights: [{ providerKey: '995084138', displayName: 'Heel Sole Podiatry PC', rate: 80.72, percentile: 68, vsMedian: 1.57, aboveP25: 29.27, rateLow: 80.72, rateHigh: 80.72 }],
  })
  const byNpi = (groups: RateGroup[]) => {
    const result = makeResult(groups)
    return { ...result, query: { ...result.query, npi: '1801474036' } }
  }
  const byName = (groups: RateGroup[], provider: string) => {
    const result = makeResult(groups)
    return { ...result, query: { ...result.query, provider } }
  }

  it('needs an NPI, EIN or name filter', () => {
    expect(hasProviderFilter(makeResult([office]))).toBe(false)
    expect(hasProviderFilter(byNpi([office]))).toBe(true)
    expect(hasProviderFilter(byName([office], 'heel'))).toBe(true)
  })

  it('says "your price" when an NPI or EIN pins one provider, and just "price" for a name search', () => {
    const [line] = yourRateLines(byNpi([office]))
    expect(line).toMatchObject({ provider: 'Heel Sole Podiatry PC', providerKey: '995084138', rate: 80.72 })
    expect(line!.text).toBe('Your price $80.72: higher than most, $29.27 more than typical. Typical price: $51.45.')
    expect(yourRateLines(byName([office], 'heel'))[0]!.text).toBe(
      'Price $80.72: higher than most, $29.27 more than typical. Typical price: $51.45.',
    )
    expect(highlightHeading(byNpi([office]))).toBe('How your provider compares')
    expect(highlightHeading(byName([office], 'heel'))).toBe('How providers whose name contains "heel" compare')
  })

  it('gives just the dollars for a price in the middle band but well away from typical', () => {
    const middle = { ...office, stats: { ...office.stats, p75: 90 } }
    expect(yourRateLines(byNpi([middle]))[0]!.text).toBe('Your price $80.72: $29.27 more than typical. Typical price: $51.45.')
  })

  it('calls the typical price the most common one only when the quarter marks prove it', () => {
    const feeSchedule = makeGroup({
      key: 'fee-schedule',
      stats: { min: 143.75, p10: 167.09, p25: 167.09, median: 167.09, p75: 167.09, p90: 167.09, max: 185.4, spreadMultiple: 1.29, p90ToP10: 1 },
      highlights: [{ providerKey: 'a', displayName: 'Downtown Gastroenterology Assoc', rate: 167.09, percentile: 50, vsMedian: 1, aboveP25: 0, rateLow: 167.09, rateHigh: 167.09 }],
    })
    expect(yourRateLines(byNpi([feeSchedule]))[0]!.text).toBe('Your price $167.09: about typical, and the most common price. Typical price: $167.')
    const spread = { ...feeSchedule, stats: { ...feeSchedule.stats, p25: 150, p75: 180 } }
    expect(yourRateLines(byNpi([spread]))[0]!.text).not.toContain('most common')
  })

  it("adds the provider's own range when it has several prices in the group, even off the table's page", () => {
    const several = { ...office, highlights: [{ ...office.highlights[0]!, rateLow: 57.05, rateHigh: 94.5 }] }
    expect(yourRateLines(byNpi([several]))[0]!.text).toBe(
      'Your price $80.72: higher than most, $29.27 more than typical. Typical price: $51.45. Your prices here run from $57.05 to $94.50.',
    )
    expect(yourRateLines(byName([several], 'heel'))[0]!.text).toContain('Its prices here run from $57.05 to $94.50.')
  })

  it('groups the lines and says when a group shows only its first matches', () => {
    const capped = { ...office, matchedProviders: 12, highlights: Array.from({ length: 5 }, (_, i) => ({ ...office.highlights[0]!, providerKey: `p${i}` })) }
    const complete = { ...office, key: 'complete', matchedProviders: 1 }
    const [first, second] = yourRateGroups(byName([capped, complete], 'podiatry'))
    expect(first).toMatchObject({ groupKey: office.key, capNote: 'Showing 5 of 12 matching providers; the table lists them all.' })
    expect(first!.lines).toHaveLength(5)
    expect(second).toMatchObject({ groupKey: 'complete', capNote: null })
  })

  it('says there is too little to compare under 5 providers, instead of inventing a position', () => {
    const small = { ...singleEr, highlights: [{ providerKey: 'x', displayName: 'Lone Hospital', rate: 4803.34, percentile: null, vsMedian: null, aboveP25: null, rateLow: 4803.34, rateHigh: 4803.34 }] }
    expect(yourRateLines(byNpi([small]))[0]!.text).toBe('Your price $4,803.34. Only 1 provider: too few to compare.')
  })

  it('never uses a statistics word', () => {
    const lowGroup = { ...office, highlights: [{ ...office.highlights[0]!, rate: 30.1 }] }
    for (const line of yourRateLines(byNpi([office, lowGroup]))) expect(line.text).not.toMatch(BANNED)
  })
})

describe('plans lens', () => {
  const lines = spreadLines(makeResult([makeGroup(), essentialBasic, singleEr]))

  it('measures how much prices vary only where there are 10+ providers', () => {
    expect(lines.map((line) => line.providers)).toEqual([10])
    expect(lines[0]!.ratio).toBeCloseTo(5921.67 / 958.31)
    expect(spreadText(lines[0]!)).toBe(
      'Even leaving out the extremes, the high end is about 6.2 times the low end. $1,401 less per case at a lower-priced provider.',
    )
    expect(spreadText(lines[0]!)).not.toMatch(BANNED)
  })

  it('puts where prices vary most first, leaving out cards where most providers get about the same price', () => {
    const narrow = { ...lines[0]!, groupKey: 'narrow', ratio: 1.43, providers: 100 }
    const flat = { ...lines[0]!, groupKey: 'flat', ratio: 1, providers: 51 }
    const nearlyFlat = { ...lines[0]!, groupKey: 'nearly-flat', ratio: 1.04, providers: 60 }
    expect(widestSpread([narrow, flat, nearlyFlat, lines[0]!]).map((line) => line.groupKey)).toEqual([lines[0]!.groupKey, 'narrow'])
    expect(widestSpread([flat])).toEqual([])
  })
})

describe('employers lens', () => {
  // The 45378 Ambetter surgery-center card: typical $578.31 (shown "$578"), lower quarter $454.44 (shown "$454").
  const ascStats = { ...makeGroup().stats, p10: 371.78, p25: 454.44, median: 578.31, p75: 627.5, p90: 663.64 }
  const asc = makeGroup({ key: 'asc', settingLabel: 'Ambulatory surgery center', providerCount: 31, lowerQuartileGap: 123.87, stats: ascStats })
  const feeSchedule = { ...asc, key: 'fee-schedule', providerCount: 51, lowerQuartileGap: 0, stats: { ...ascStats, p25: 167.09, median: 167.09 } }

  it('lists the typical price and how much less per case at a lower-priced provider, largest groups first', () => {
    const lines = costLines(makeResult([essentialBasic, asc, singleEr]))
    // The gap is the card's: "$578" less "$454" as shown, $124.
    expect(lines.map((line) => [line.providers, line.typical, line.gap])).toEqual([
      [31, 578.31, 124],
      [8, 3595.92, null],
    ])
    expect(lines.every((line) => line.yearly === null)).toBe(true)
  })

  it("turns the reader's cases a year into a rounded yearly figure only where there is a gap", () => {
    const [first, second] = costLines(makeResult([asc, essentialBasic]), 1200)
    expect(first!.yearly).toBe(124 * 1200)
    expect(yearlyText(first!, 1200)).toBe('About $149,000 less a year for your 1,200 cases at lower-priced providers.')
    expect(second!.yearly).toBeNull()
  })

  it('says what a member pays and how often a service happens only in the footer, not here', () => {
    expect(EMPLOYER_CAVEATS).toHaveLength(2)
    for (const caveat of EMPLOYER_CAVEATS) expect(caveat).not.toMatch(/member pays|claim counts|how often/i)
  })

  it('never multiplies a per-unit price by cases: a case can use several units', () => {
    const contrast = { ...asc, key: 'contrast', perUnit: true, lowerQuartileGap: 0.04, stats: { ...ascStats, p25: 0.13, median: 0.17 } }
    const [line] = costLines(makeResult([contrast]), 100)
    expect(line).toMatchObject({ perUnit: true, gap: 0.04, yearly: null })
  })

  it('says why there is no yearly figure: too few providers, or a lower-priced quarter at the typical price', () => {
    expect(noYearlyReason(costLines(makeResult([essentialBasic]), 100))).toBe('No yearly figure here: it needs a card with 10 or more providers.')
    expect(noYearlyReason(costLines(makeResult([feeSchedule]), 100))).toBe(
      'No yearly figure here: the lower-priced quarter of providers get the typical price, so there is nothing less to count.',
    )
    expect(noYearlyReason(costLines(makeResult([feeSchedule, essentialBasic]), 100))).toBe(
      "No yearly figure here: cards with fewer than 10 providers can't show one, and on the others the lower-priced quarter of providers get the typical price.",
    )
  })

  it('speaks for the picked group when it has a gap, else the largest group with one', () => {
    const essential = { ...asc, key: 'essential-asc', providerCount: 27, lowerQuartileGap: 48, stats: { ...ascStats, p25: 433, median: 481 } }
    const lines = costLines(makeResult([feeSchedule, asc, essential]), 12)
    expect(yearlyLead(lines)?.groupKey).toBe('asc')
    expect(yearlyLead(lines, 'essential-asc')?.groupKey).toBe('essential-asc')
    // A picked group without a gap falls back to the largest one that has one.
    expect(yearlyLead(lines, 'fee-schedule')?.groupKey).toBe('asc')
    expect(yearlyLead(costLines(makeResult([feeSchedule]), 12))).toBeNull()
  })
})

describe('innovators lens', () => {
  it('puts the clean-up numbers in plain words, leaving out counts of zero', () => {
    expect(qualityLines(makeResult([]).quality)).toEqual(['6,642 published rows became 28 unique rates.', '6,614 exact duplicates collapsed.'])
    const quality = { publishedRows: 22193, uniqueRates: 549, duplicatesCollapsed: 21644, providersWithMultipleRates: 31, maskedTaxIds: 24, wholeTaxIdContracts: 1 }
    expect(qualityLines(quality)).toEqual([
      '22,193 published rows became 549 unique rates.',
      '21,644 exact duplicates collapsed.',
      '24 tax IDs masked: prefixes the IRS never issues.',
      '1 whole-tax-ID contract: no individual NPIs listed.',
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
    feeType: 'professional',
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
    feeType: 'professional',
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

  it('prices each site of care per unit: the typical price from 5 providers, the prices themselves under 5', () => {
    const lines = unitPriceLines(makeResult([office, small]))
    expect(lines.map((line) => [line.site, unitPriceText(line)])).toEqual([
      ["Doctor's office · Doctor's fee", '$7.16, $7.16, $8.46, $9.77'],
      ["Doctor's office · Doctor's fee · Drug amount discarded (modifier JW)", 'Typical $6.97'],
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
