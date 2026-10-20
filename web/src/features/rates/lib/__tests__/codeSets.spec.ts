import { describe, expect, it } from 'vitest'
import type { CodeInfo, DoseUnit, LevelLadder, LevelLine, RateGroup, RateSearchResult, RateStats } from '../../api/types'
import { doseMath, dosePriceOptions, doseUnits } from '../dose'
import { comparableLines, defaultLine, ladderCaption, ladderSteps, levelLabel, lineLabel } from '../levels'
import { pricingNotes } from '../pricing'

const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|index|comparison group/i

function stats(median: number, overrides: Partial<RateStats> = {}): RateStats {
  return { min: median * 0.5, p10: median * 0.7, p25: median * 0.85, median, p75: median * 1.2, p90: median * 1.4, max: median * 2, spreadMultiple: 4, p90ToP10: 2, ...overrides }
}

function group(overrides: Partial<RateGroup> = {}): RateGroup {
  return {
    key: 'essential-plan|HCPCS|J2704|professional|office||negotiated',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'HCPCS',
    code: 'J2704',
    feeType: 'professional',
    feeTypeLabel: 'Professional fee',
    settingKey: 'office',
    settingLabel: "Doctor's office",
    settingNote: null,
    places: [],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per HCPCS unit',
    perUnit: true,
    billedAs: [],
    providerCount: 12,
    rateCount: 12,
    statsLevel: 'full',
    stats: stats(0.09, { p10: 0.07, p90: 0.12 }),
    lowerQuartileGap: 0.01,
    listedRates: [],
    matchedProviders: 0,
    highlights: [],
    notes: [],
    ...overrides,
  }
}

function code(overrides: Partial<CodeInfo> = {}): CodeInfo {
  return {
    codeType: 'HCPCS',
    code: 'J2704',
    name: 'Injection, propofol, 10 mg',
    description: null,
    descriptionSource: 'cms_hcpcs',
    category: { key: 'drugs', label: 'Drugs & biologicals', unit: 'per HCPCS unit', perUnit: true },
    classification: { key: 'RI', category: 'Treatments', subcategory: 'Injections and Infusions (nononcologic)', family: null, source: 'cms_rbcs' },
    doseUnit: { amount: 10, unit: 'mg', text: '10 mg' },
    levelFamily: null,
    foundVia: [],
    ...overrides,
  }
}

function result(groups: RateGroup[], info: CodeInfo | null = code()): RateSearchResult {
  return {
    query: {
      code: info?.code ?? 'J2704', matchedCodes: [], codeType: null, npi: null, ein: null, product: null, setting: null, feeType: null,
      provider: null, specialty: null, county: null, countyPlace: null, group: null, sort: 'rate', page: 1, pageSize: 25,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code: info,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

describe('dose math', () => {
  const propofol: DoseUnit = { amount: 10, unit: 'mg', text: '10 mg' }
  const botox: DoseUnit = { amount: 1, unit: 'unit', text: '1 unit' }

  it('rounds a dose up to whole HCPCS units', () => {
    expect(doseUnits(150, propofol)).toEqual({ exact: 15, units: 15, roundedUp: false })
    expect(doseUnits(125, propofol)).toEqual({ exact: 12.5, units: 13, roundedUp: true })
    expect(doseUnits(0.3, { amount: 0.1, unit: 'mg', text: '0.1 mg' })).toEqual({ exact: 3, units: 3, roundedUp: false })
  })

  it('states the math in plain words, from the prices as shown', () => {
    const math = doseMath(125, propofol, 0.0912, { low: 0.07, high: 0.12 })!
    expect(math.units).toBe(13)
    expect(math.lines).toEqual([
      '125 mg divided by 10 mg is 12.5. Units are billed whole, so that\'s 13 units.',
      'At the typical price of $0.09 a unit, 13 units come to $1.17.',
      'Most providers get between $0.07 and $0.12 a unit: $0.91 to $1.56 for this dose.',
    ])
    expect(math.lines.join(' ')).not.toMatch(BANNED)
  })

  it('says a whole number of units plainly, and "most providers" once when they all get the same', () => {
    expect(doseMath(40, propofol, 0.95, { low: 0.95, high: 0.95 })!.lines).toEqual([
      '40 mg divided by 10 mg is 4 units.',
      'At the typical price of $0.95 a unit, 4 units come to $3.80.',
      'Most providers get $0.95 a unit: $3.80 for this dose.',
    ])
  })

  it('says only the rounding for codes whose unit is one unit, and leaves out "most providers" under 10', () => {
    expect(doseMath(100, botox, 5.52)!.lines).toEqual(["That's 100 units.", 'At the typical price of $5.52 a unit, 100 units come to $552.'])
    expect(doseMath(2.5, botox, 5.52)!.lines[0]).toBe("2.5 units: units are billed whole, so that's 3 units.")
    expect(doseMath(1, botox, 5.52)!.lines[1]).toBe('At the typical price of $5.52 a unit, 1 unit comes to $5.52.')
  })

  it('shows tiny unit prices to four places instead of $0', () => {
    expect(doseMath(1000, { amount: 1, unit: 'ml', text: 'per ml' }, 0.0035)!.lines[1]).toBe('At the typical price of $0.0035 a unit, 1,000 units come to $3.50.')
  })

  it('has nothing to say for a dose that is not a positive number', () => {
    expect(doseMath(0, propofol, 1)).toBeNull()
    expect(doseMath(Number.NaN, propofol, 1)).toBeNull()
  })

  it("prices from cards billed per unit under the drug's own code, without modifiers first", () => {
    const options = dosePriceOptions(
      result([
        group({ key: 'jw', modifiers: [{ code: 'JW', label: 'Drug amount discarded' }], providerCount: 40 }),
        group({ key: 'office', providerCount: 12 }),
        group({ key: 'small', providerCount: 3, stats: stats(0.1) }),
        group({ key: 'facility', perUnit: false, unit: 'per facility charge', billedAs: [{ billingCodeType: 'RC', billingCode: '0636', label: 'Revenue code 0636' }] }),
        group({ key: 'basic', providerCount: 6 }),
      ]),
    )
    expect(options.map((option) => option.groupKey)).toEqual(['office', 'basic', 'jw'])
    expect(options[0]!.most).toEqual({ low: 0.07, high: 0.12 })
    expect(options[1]!.most).toBeNull()
  })
})

describe('how it is priced', () => {
  it('explains drugs per HCPCS unit with the code’s own unit', () => {
    const notes = pricingNotes(result([group()]))
    expect(notes.map((note) => note.key)).toEqual(['drugs'])
    expect(notes[0]!.text).toContain('(for J2704, 10 mg)')
  })

  it('adds facility rates under revenue codes, health centers, the imaging split and visit levels where they apply', () => {
    const imaging = code({ code: '70553', codeType: 'CPT', category: { key: 'imaging', label: 'Imaging', unit: 'per study', perUnit: false }, doseUnit: null })
    const facility = group({ billedAs: [{ billingCodeType: 'RC', billingCode: '0490', label: 'Revenue code 0490' }], codeType: 'CPT' })
    expect(pricingNotes(result([facility, group({ modifiers: [{ code: '26', label: 'Professional component' }] })], imaging)).map((n) => n.key)).toEqual([
      'facility-rates',
      'imaging-split',
    ])

    const visit = code({ code: '99213', codeType: 'CPT', category: { key: 'visits', label: 'Visits', unit: 'per visit', perUnit: false }, doseUnit: null, levelFamily: 'Office visit, established patient' })
    expect(pricingNotes(result([group({ settingKey: 'fqhc' })], visit)).map((n) => n.key)).toEqual(['health-centers', 'levels'])

    const anesthesia = code({ code: '00812', codeType: 'CPT', category: { key: 'anesthesia', label: 'Anesthesia', unit: 'per anesthesia unit', perUnit: true }, doseUnit: null })
    expect(pricingNotes(result([], anesthesia))[0]!.text).toContain('usually one for each 15 minutes')

    const revenueCode = code({ code: '0450', codeType: 'RC', category: { key: 'facility', label: 'Facility', unit: 'per facility charge', perUnit: false }, doseUnit: null })
    expect(pricingNotes(result([], revenueCode)).map((n) => n.key)).toEqual(['revenue-code'])
  })

  it('uses no statistics words', () => {
    const visit = code({ code: '99213', levelFamily: 'Office visit, established patient', category: { key: 'anesthesia', label: '', unit: '', perUnit: true } })
    const text = pricingNotes(result([group({ settingKey: 'rhc' })], visit)).map((note) => `${note.title} ${note.text}`).join(' ')
    expect(text).not.toMatch(BANNED)
    expect(pricingNotes(result([], null))).toEqual([])
  })
})

describe('level ladder', () => {
  const step = (code: string, level: number, providers: number, median: number, line = 'office') => ({
    code,
    level,
    groupKey: `essential-plan|CPT|${code}|professional|${line}|25|negotiated`,
    providerCount: providers,
    statsLevel: providers >= 10 ? 'full' : providers >= 5 ? 'basic' : 'list',
    stats: stats(median, providers >= 10 ? {} : { p10: null, p25: null, p75: null, p90: null }),
  })

  function line(key: string, levels: ReturnType<typeof step>[], overrides: Partial<LevelLine> = {}): LevelLine {
    return {
      key,
      productKey: 'essential-plan',
      productLabel: 'Essential Plan',
      feeType: 'professional',
      settingKey: 'office',
      settingLabel: "Doctor's office",
      modifiers: [{ code: '25', label: 'Separate service, same day' }],
      negotiatedType: 'negotiated',
      unit: 'per visit',
      levels,
      ...overrides,
    }
  }

  const office = line('office', [step('99211', 1, 167, 29.88), step('99212', 2, 87, 71.02), step('99213', 3, 105, 113.59), step('99215', 5, 3, 140)])
  const telehealth = line('telehealth', [step('99213', 3, 300, 86.1, 'tele'), step('99214', 4, 8, 121.96, 'tele')], { settingLabel: 'Telehealth, patient at home', modifiers: [] })
  const lonely = line('lonely', [step('99213', 3, 50, 90, 'lonely')])
  const ladder: LevelLadder = {
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    family: 'Office visit, established patient',
    codes: ['99211', '99212', '99213', '99214', '99215'].map((code, index) => ({ code, level: index + 1 })),
    lines: [office, telehealth, lonely],
  }

  it('keeps only lines with a typical price at two levels or more', () => {
    expect(comparableLines(ladder).map((candidate) => candidate.key)).toEqual(['office', 'telehealth'])
    expect(comparableLines(null)).toEqual([])
  })

  it('shows the picked card’s line, else the fullest ladder with a typical price at the searched level', () => {
    const lines = comparableLines(ladder)
    expect(defaultLine(lines, '99213')!.key).toBe('office')
    expect(defaultLine(lines, '99214')!.key).toBe('telehealth')
    expect(defaultLine(lines, '99213', 'essential-plan|CPT|99212|professional|office|25|negotiated')!.key).toBe('office')
    expect(defaultLine([], '99213')).toBeNull()
  })

  it('gives every level a step in plain words, even where the line has no prices', () => {
    const steps = ladderSteps(ladder, office)
    expect(steps.map((s) => [levelLabel(s), s.typical, s.text])).toEqual([
      ['Level 1 (99211)', 29.88, 'Typical price: $29.88. Most providers get between $20.92 and $41.83.'],
      ['Level 2 (99212)', 71.02, 'Typical price: $71.02. Most providers get between $49.71 and $99.43.'],
      ['Level 3 (99213)', 113.59, 'Typical price: $114. Most providers get between $79.51 and $159.'],
      ['Level 4 (99214)', null, 'No prices for this level here.'],
      ['Level 5 (99215)', null, 'Only 3 providers: too few to compare.'],
    ])
    expect(steps[0]!.most).toEqual({ low: 29.88 * 0.7, high: 29.88 * 1.4 })
    expect(steps[0]!.range).toBe('Most providers get between $20.92 and $41.83.')
    expect(steps[3]!.range).toBeNull()
    expect(ladderSteps(ladder, telehealth)[3]!.range).toBe('Prices run from $60.98 to $244.')
    expect(ladderSteps(ladder, telehealth)[3]!.text).toBe('Typical price: $122. Prices run from $60.98 to $244.')
    expect(ladderCaption(steps)).toBe("Each level's typical price comes from 87 to 167 providers.")
    expect(lineLabel(office)).toBe("Essential Plan · Doctor's fee · Doctor's office · Separate service, same day (modifier 25)")
    expect(steps.map((s) => s.text).join(' ')).not.toMatch(BANNED)
  })
})
