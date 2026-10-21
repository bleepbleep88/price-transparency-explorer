import { describe, expect, it } from 'vitest'
import type { CodeInfo, DoseUnit, LevelLadder, LevelLine, RateGroup, RateSearchResult, RateStats } from '../../api/types'
import { doseLine, dosePricing, doseRange, doseUnits, unitDefinition } from '../dose'
import { chooseLine, comparableLines, ladderSteps, levelLabel, lineLabel, lineTitle } from '../levels'
import { pricingNotes } from '../pricing'
import { parseDose } from '../searchParams'

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
  const botoxUnit = () => botox

  it('rounds a dose up to whole HCPCS units, at least 1', () => {
    expect(doseUnits(150, propofol)).toEqual({ exact: 15, units: 15, roundedUp: false })
    expect(doseUnits(125, propofol)).toEqual({ exact: 12.5, units: 13, roundedUp: true })
    expect(doseUnits(0.3, { amount: 0.1, unit: 'mg', text: '0.1 mg' })).toEqual({ exact: 3, units: 3, roundedUp: false })
    expect(doseUnits(0.0000001, propofol)).toEqual({ exact: 0, units: 1, roundedUp: true })
  })

  it('answers a dose in one line: dose = units → cost, from the price as shown', () => {
    expect(doseLine(45, { amount: 10, unit: 'mg', text: '10 mg' }, 0.95)).toEqual({ text: '45 mg = 5 units → $4.75', units: 5, cost: 4.75 })
    expect(doseLine(125, propofol, 0.0912)!.text).toBe('125 mg = 13 units → $1.17')
    expect(doseLine(0.5, propofol, 0.95)!.text).toBe('0.5 mg = 1 unit → $0.95')
    expect(doseLine(0.000001, propofol, 0.95)!.units).toBe(1)
  })

  it('leaves out "= N units" for a code whose unit is one unit, unless it rounds up', () => {
    expect(doseLine(100, botox, 5.52)!.text).toBe('100 units → $552')
    expect(doseLine(2.5, botox, 5.52)!.text).toBe('2.5 units = 3 units → $16.56')
    expect(unitDefinition(botox)).toBeNull()
    expect(unitDefinition(propofol)).toBe('1 unit = 10 mg')
  })

  it('takes only plain decimals, with thousands commas', () => {
    expect(parseDose('45')).toBe(45)
    expect(parseDose(' 2.5 ')).toBe(2.5)
    expect(parseDose('.5')).toBe(0.5)
    expect(parseDose('2,400,000')).toBe(2400000)
    for (const text of ['0x10', '1e3', 'Infinity', '-3', '0', '45 mg', '1,00', '1.0000001', '', '.']) expect(parseDose(text)).toBeNull()
  })

  it('refuses more than 100,000 units for one dose, and anything that is not a positive dose', () => {
    const penicillin: DoseUnit = { amount: 100000, unit: 'unit', text: '100,000 units' }
    expect(doseLine(2400000, penicillin, 12)!.units).toBe(24)
    expect(doseLine(100001, botoxUnit(), 5)).toBeNull()
    expect(doseLine(0, propofol, 1)).toBeNull()
    expect(doseLine(Number.NaN, propofol, 1)).toBeNull()
  })

  it('does not total a price that shows as $0, and gives "most places" as an en-dash range', () => {
    expect(doseLine(1000, { amount: 1, unit: 'ml', text: '1 ml' }, 0)).toEqual({ text: '1,000 ml = 1,000 units → under 1¢ a unit', units: 1000, cost: null })
    expect(doseRange(13, { low: 0.07, high: 0.12 })).toBe('Most places: $0.91 – $1.56')
    expect(doseRange(4, { low: 0.95, high: 0.95 })).toBe('Most places: $3.80')
    expect(doseRange(4, null)).toBeNull()
    expect(doseLine(45, propofol, 0.95)!.text).not.toMatch(BANNED)
  })

  const discarded = [{ code: 'JW', label: 'Drug amount discarded' }]

  it("prices a given dose from cards billed per unit under the drug's own code, in dollars, never discarded drug", () => {
    const { options, reason } = dosePricing(
      result([
        group({ key: 'jw', modifiers: discarded, providerCount: 40 }),
        group({ key: 'jz', modifiers: [{ code: 'JZ', label: 'No drug discarded' }], providerCount: 30 }),
        group({ key: 'office', providerCount: 12 }),
        group({ key: 'small', providerCount: 3, stats: stats(0.1) }),
        group({ key: 'facility', perUnit: false, unit: 'per facility charge', billedAs: [{ billingCodeType: 'RC', billingCode: '0636', label: 'Revenue code 0636' }] }),
        group({ key: 'percent', negotiatedType: 'percentage', providerCount: 20 }),
        group({ key: 'basic', providerCount: 6 }),
      ]),
    )
    expect(options.map((option) => option.groupKey)).toEqual(['office', 'basic'])
    expect(options[0]!.most).toEqual({ low: 0.07, high: 0.12 })
    expect(options[1]!.most).toBeNull()
    expect(options[0]!.label).toBe("Essential Plan · Doctor · in the office")
    expect(reason).toBeNull()
  })

  it("prices from the reader's plan and tile, never another plan's price (J1100: Essential $0.11, Ambetter $0.09)", () => {
    const j1100 = (plan: 'essential-plan' | 'ambetter', overrides: Partial<RateGroup> = {}) =>
      group({
        key: `${plan}|HCPCS|J1100|professional|office||negotiated`,
        productKey: plan,
        productLabel: plan === 'ambetter' ? 'Ambetter from Fidelis Care' : 'Essential Plan',
        code: 'J1100',
        stats: stats(plan === 'ambetter' ? 0.09 : 0.11, { p10: null, p90: null }),
        ...overrides,
      })
    // Ambetter has more places, so with no plan it would lead.
    const groups = [j1100('ambetter', { providerCount: 40 }), j1100('essential-plan', { providerCount: 12 })]
    const essential = dosePricing(result(groups), { plan: 'essential-plan' })
    expect(essential.options.map((option) => option.typical)).toEqual([0.11])
    expect(doseLine(8, { amount: 1, unit: 'mg', text: '1 mg' }, essential.options[0]!.typical)!.text).toBe('8 mg = 8 units → $0.88')
    expect(dosePricing(result(groups), { plan: 'ambetter' }).options.map((option) => option.typical)).toEqual([0.09])
    expect(dosePricing(result(groups)).options.map((option) => option.typical)).toEqual([0.09, 0.11])

    // The reader's tile leads its plan, even with a modifier and fewer places.
    const injected = j1100('essential-plan', { key: 'tile', modifiers: [{ code: 'JA', label: 'Given into a vein' }], providerCount: 6, stats: stats(0.15, { p10: null, p90: null }) })
    expect(dosePricing(result([...groups, injected]), { plan: 'essential-plan', group: 'tile' }).options.map((option) => option.groupKey)).toEqual([
      'tile',
      'essential-plan|HCPCS|J1100|professional|office||negotiated',
    ])
    // A tile that can't price a dose (discarded drug) leaves the plan's best card first.
    const thrownAway = j1100('essential-plan', { key: 'jw-tile', modifiers: discarded, providerCount: 50 })
    expect(dosePricing(result([...groups, thrownAway]), { plan: 'essential-plan', group: 'jw-tile' }).options[0]!.typical).toBe(0.11)
  })

  it("says why the reader's plan can't price a dose, even when another plan can", () => {
    const groups = [group({ key: 'ambetter', productKey: 'ambetter' }), group({ key: 'essential-jw', modifiers: discarded, providerCount: 7 })]
    expect(dosePricing(result(groups), { plan: 'essential-plan' })).toEqual({ options: [], reason: 'Only priced for thrown-away drug. No dose price.' })
    // A plan with no per-unit price at all: no reason, the calculator stays away.
    expect(dosePricing(result(groups), { plan: 'other-plan' })).toEqual({ options: [], reason: null })
  })

  it('says why it can price no dose: only discarded drug has a typical price, or too few providers', () => {
    expect(dosePricing(result([group({ modifiers: discarded, providerCount: 7 }), group({ key: 'small', providerCount: 2 })]))).toEqual({
      options: [],
      reason: 'Only priced for thrown-away drug. No dose price.',
    })
    expect(dosePricing(result([group({ providerCount: 3 }), group({ key: 'b', providerCount: 4 })])).reason).toBe(
      'Too few prices to work out a dose.',
    )
    // No per-unit price at all (only facility charges): nothing to say, the calculator stays away.
    expect(dosePricing(result([group({ perUnit: false })]))).toEqual({ options: [], reason: null })
  })
})

describe('how it is priced', () => {
  it('explains drugs per HCPCS unit with the code’s own unit', () => {
    const notes = pricingNotes(result([group()]))
    expect(notes.map((note) => note.key)).toEqual(['drugs'])
    expect(notes[0]!.text).toContain('(for J2704, 10 mg)')
    expect(pricingNotes(result([group()], code({ code: 'Q9967', doseUnit: { amount: 1, unit: 'ml', text: '1 ml' } })))[0]!.text).toContain('(for Q9967, 1 ml)')
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
    expect(chooseLine(ladder, '99213').line!.key).toBe('office')
    expect(chooseLine(ladder, '99214').line!.key).toBe('telehealth')
    expect(chooseLine(ladder, '99213', { group: 'essential-plan|CPT|99212|professional|office|25|negotiated' })).toMatchObject({ line: office, note: null })
    expect(chooseLine(null, '99213')).toEqual({ lines: [], line: null, note: null })
  })

  it("follows the search's plan, setting and fee type, and offers only those lines", () => {
    const ambetter = line('ambetter', office.levels, { productKey: 'ambetter', productLabel: 'Ambetter from Fidelis Care' })
    const facility = line('facility', office.levels, { feeType: 'facility', modifiers: [] })
    const wide: LevelLadder = { ...ladder, lines: [office, telehealth, ambetter, facility] }
    expect(chooseLine(wide, '99213', { product: 'ambetter' }).lines.map((l) => l.key)).toEqual(['ambetter'])
    expect(chooseLine(wide, '99213', { setting: 'telehealth-home' }).lines).toEqual([])
    expect(chooseLine({ ...wide, lines: [office, { ...telehealth, settingKey: 'telehealth-home' }] }, '99213', { setting: 'telehealth-home' }).line!.key).toBe('telehealth')
    expect(chooseLine(wide, '99213', { feeType: 'facility' }).line!.key).toBe('facility')
  })

  it("stands in the closest line when the picked card's can't be compared, and says so", () => {
    const picked = chooseLine(ladder, '99213', { group: 'essential-plan|CPT|99213|professional|lonely|25|negotiated' })
    expect(picked.line!.key).toBe('office')
    expect(picked.note).toBe('Too few levels for this bill. Showing a similar one.')

    // A lone Ambetter video visit: the Ambetter video line stands in, not the fuller Essential Plan office line.
    const ambetterLonely = line('ambetter-lonely', [step('99213', 3, 50, 90, 'ambetter-lonely')], { productKey: 'ambetter', settingKey: 'telehealth-home' })
    const ambetterVideo = line('ambetter-video', telehealth.levels, { productKey: 'ambetter', settingKey: 'telehealth-home' })
    const ambetterOffice = line('ambetter-office', office.levels, { productKey: 'ambetter' })
    const wide: LevelLadder = { ...ladder, lines: [office, ambetterOffice, ambetterVideo, ambetterLonely] }
    expect(chooseLine(wide, '99213', { group: 'essential-plan|CPT|99213|professional|ambetter-lonely|25|negotiated' }).line!.key).toBe('ambetter-video')
  })

  it('names a line plainly up front, and in full (modifiers, rate type) in the picker', () => {
    expect(lineTitle(office)).toBe('Essential Plan · Doctor · in the office')
    expect(lineLabel(office)).toBe('Essential Plan · Doctor · in the office · same day as a procedure')
    expect(lineLabel({ ...office, negotiatedType: 'fee schedule' })).toBe('Essential Plan · Doctor · in the office · same day as a procedure · fee schedule rate')
  })

  it('gives every level a step, even where the line has no prices', () => {
    const steps = ladderSteps(ladder, office)
    expect(steps.map((s) => [levelLabel(s), s.typical, s.mostText, s.text])).toEqual([
      ['Level 1', 29.88, '$20.92 – $41.83', 'Typical $29.88 · 167 places'],
      ['Level 2', 71.02, '$49.71 – $99.43', 'Typical $71.02 · 87 places'],
      ['Level 3', 113.59, '$79.51 – $159', 'Typical $114 · 105 places'],
      ['Level 4', null, null, 'No prices'],
      ['Level 5', null, null, 'Only 3 places'],
    ])
    expect(steps[0]!.most).toEqual({ low: 29.88 * 0.7, high: 29.88 * 1.4 })
    // 5 to 9 places: a typical price, no "most places".
    expect(ladderSteps(ladder, telehealth)[3]).toMatchObject({ typical: 121.96, mostText: null, text: 'Typical $122 · 8 places' })
    expect(steps.map((s) => s.text).join(' ')).not.toMatch(BANNED)
  })
})
