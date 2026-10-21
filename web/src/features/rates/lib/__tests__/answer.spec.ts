import { describe, expect, it } from 'vitest'
import type { RateGroup, RateSearchResult } from '../../api/types'
import { currentPlan, leadGroup, otherPrices, planName, planTabs, roleLines, shortRange, tileFor, tileGroups } from '../answer'

// Colonoscopy (45378) as the Sep 28 files have it: Essential Plan's doctor at a surgery center (116 providers) and the
// surgery center's own bill (27), Ambetter's surgery center (29) and a doctor's office card with 3.
function group(overrides: Partial<RateGroup>): RateGroup {
  return {
    key: 'essential-plan|CPT|45378|facility|asc||negotiated',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
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
    providerCount: 27,
    rateCount: 27,
    statsLevel: 'full',
    stats: { min: 247.85, p10: 389.37, p25: 450.92, median: 510.83, p75: 600, p90: 624.85, max: 688, spreadMultiple: 2.78, p90ToP10: 1.6 },
    lowerQuartileGap: 59.91,
    listedRates: [],
    matchedProviders: 0,
    highlights: [],
    notes: [],
    ...overrides,
  }
}

const essentialCenter = group({})
const essentialDoctor = group({
  key: 'essential-plan|CPT|45378|professional|asc|33|negotiated',
  feeType: 'professional',
  feeTypeLabel: 'Professional (clinician) fee',
  modifiers: [{ code: '33', label: 'Preventive service (modifier 33)' }],
  providerCount: 116,
  stats: { min: 143.75, p10: 167.09, p25: 167.09, median: 168.07, p75: 168.07, p90: 174.2, max: 185.4, spreadMultiple: 1.29, p90ToP10: 1.04 },
  lowerQuartileGap: 0.98,
})
const ambetterCenter = group({
  key: 'ambetter-from-fidelis-care|CPT|45378|facility|asc||negotiated',
  productKey: 'ambetter-from-fidelis-care',
  productLabel: 'Ambetter from Fidelis Care',
  providerCount: 29,
  stats: { min: 226.51, p10: 363.8, p25: 433.08, median: 568.65, p75: 605.93, p90: 645.43, max: 1154.16, spreadMultiple: 5.1, p90ToP10: 1.77 },
  lowerQuartileGap: 135.57,
})
const ambetterOffice = group({
  key: 'ambetter-from-fidelis-care|CPT|45378|professional|office|SU|negotiated',
  productKey: 'ambetter-from-fidelis-care',
  productLabel: 'Ambetter from Fidelis Care',
  feeType: 'professional',
  settingKey: 'office',
  settingLabel: "Doctor's office",
  modifiers: [{ code: 'SU', label: "Equipment used in a doctor's office (modifier SU)" }],
  providerCount: 3,
  statsLevel: 'list',
  stats: { min: 300, p10: null, p25: null, median: null, p75: null, p90: null, max: 450, spreadMultiple: null, p90ToP10: null },
  lowerQuartileGap: null,
  listedRates: [450, 300, 300],
})

function result(groups: RateGroup[], overrides: Partial<RateSearchResult> = {}): RateSearchResult {
  return {
    query: {
      code: '45378', matchedCodes: ['45378'], codeType: null, npi: null, ein: null, product: null, setting: null, feeType: null,
      provider: null, specialty: null, county: null, countyPlace: null, group: null, sort: 'rate', page: 1, pageSize: 25,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-09-28', isLatest: true },
    code: null,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 13931, uniqueRates: 74, duplicatesCollapsed: 13857, providersWithMultipleRates: 8, maskedTaxIds: 1, wholeTaxIdContracts: 0 },
    ...overrides,
  }
}

const colonoscopy = result([ambetterCenter, ambetterOffice, essentialDoctor, essentialCenter])

describe('plan tabs', () => {
  it('lists the plans, the one with the most providers first, by short name', () => {
    expect(planTabs(colonoscopy)).toEqual([
      { key: 'essential-plan', label: 'Essential Plan' },
      { key: 'ambetter-from-fidelis-care', label: 'Ambetter' },
    ])
    expect(planName('Ambetter from Fidelis Care')).toBe('Ambetter')
    expect(currentPlan(colonoscopy, 'ambetter-from-fidelis-care')).toBe('ambetter-from-fidelis-care')
    expect(currentPlan(colonoscopy, 'nope')).toBe('essential-plan')
  })
})

describe('bill tiles', () => {
  it('shows one tile per bill, the strongest first', () => {
    expect(tileGroups(colonoscopy, 'essential-plan').map((g) => g.key)).toEqual([essentialDoctor.key, essentialCenter.key])
    // A price list with a typical price comes before one with only a few prices.
    expect(tileGroups(colonoscopy, 'ambetter-from-fidelis-care').map((g) => g.key)).toEqual([ambetterCenter.key, ambetterOffice.key])
    expect(leadGroup(colonoscopy, 'essential-plan')?.key).toBe(essentialDoctor.key)
    expect(leadGroup(colonoscopy, 'essential-plan', essentialCenter.key)?.key).toBe(essentialCenter.key)
  })

  it('says each bill in a few plain words', () => {
    expect(tileFor(essentialCenter, colonoscopy)).toMatchObject({ label: 'Surgery center', where: null, price: 510.83, line: 'Most: $389 – $625', count: '27 places' })
    expect(tileFor(essentialDoctor, colonoscopy)).toMatchObject({ label: 'Doctor', where: 'at a surgery center', line: 'Most: $167 – $174' })
    // Under 5 places: the prices themselves, no typical price, no count.
    expect(tileFor(ambetterOffice, colonoscopy)).toMatchObject({ price: null, listed: [300, 300, 450], count: null })
  })

  it('gives ranges only where the data can say them', () => {
    expect(shortRange({ providers: 27, min: 247.85, max: 688, p10: 389.37, p25: 450.92, median: 510.83, p75: 600, p90: 624.85 })).toBe('Most: $389 – $625')
    expect(shortRange({ providers: 7, min: 185.4, max: 205.41, p10: null, p25: 190, median: 205.41, p75: 205.41, p90: null })).toBe('From $185 to $205')
    expect(shortRange({ providers: 3, min: 300, max: 450, p10: null, p25: null, median: null, p75: null, p90: null })).toBeNull()
  })

  it('lists every other price in one short line', () => {
    const shown = tileGroups(colonoscopy, 'essential-plan').map((g) => g.key)
    expect(otherPrices(colonoscopy, shown)).toEqual([
      { groupKey: ambetterCenter.key, plan: 'Ambetter', bill: 'Surgery center', price: '$569', count: '29 places' },
      { groupKey: ambetterOffice.key, plan: 'Ambetter', bill: "Doctor · in the office · equipment used in a doctor's office", price: 'Only 3 places', count: '' },
    ])
  })
})

describe('role lines', () => {
  it('gives each role one short line about the bill picked', () => {
    // The employer line speaks for the bill with the most to gain, named: not the doctor's $1.
    expect(roleLines('employers', colonoscopy, essentialDoctor, 40, [essentialDoctor, essentialCenter])).toEqual([
      { text: 'Surgery center: the cheapest quarter of places get at least $60 less per case.', yearly: 'About $2,400 less a year for your 40 cases at lower-priced providers.' },
    ])
    expect(roleLines('employers', colonoscopy, essentialCenter, 40)).toEqual([
      { text: 'The cheapest quarter of places get at least $60 less per case.', yearly: 'About $2,400 less a year for your 40 cases at lower-priced providers.' },
    ])
    expect(roleLines('plans', colonoscopy, essentialCenter)).toEqual([{ text: 'Most places get $389 – $625. Highest: $688.' }])
    expect(roleLines('providers', colonoscopy, essentialCenter)).toEqual([{ text: 'Add your NPI or tax ID to see where you stand.', action: 'npi' }])
    expect(roleLines('innovators', colonoscopy, essentialCenter)[0]?.text).toBe('13,931 listed prices → 74 unique prices. Each links to its file.')
    expect(roleLines('life-sciences', colonoscopy, essentialCenter)).toEqual([{ text: 'Not a drug. Try Botox (J0585).', action: 'drug' }])
  })

  it('says nothing it cannot back up', () => {
    expect(roleLines('employers', colonoscopy, ambetterOffice, 40)).toEqual([{ text: 'Needs 10 places to say what cheaper ones save.' }])
    expect(roleLines('plans', colonoscopy, ambetterOffice)).toEqual([{ text: 'Too few places to compare. Needs 10.' }])
  })

  it('shows the searched provider on its bill', () => {
    const pinned = result([group({ highlights: [{ providerKey: 'x', displayName: 'Goldstep', rate: 247.85, percentile: 0, vsMedian: 0.49, aboveP25: null, rateLow: 247.85, rateHigh: 247.85 }] })], {
      query: { ...colonoscopy.query, npi: '1234567893' },
    })
    expect(tileFor(pinned.groups[0]!, pinned).yours).toBe('Yours: $247.85 · Lower than most · $263.15 less than typical')
  })
})
