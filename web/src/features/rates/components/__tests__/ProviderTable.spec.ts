import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { RateGroup, RateRow } from '../../api/types'
import ProviderTable from '../ProviderTable.vue'

// Two 45378 groups: a 31-provider surgery-center facility fee (typical $578.31) and a 3-provider doctor's fee.
function makeGroup(key: string, overrides: Partial<RateGroup> = {}): RateGroup {
  return {
    key,
    productKey: 'ambetter-from-fidelis-care',
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
    stats: { min: 226.51, p10: 371.78, p25: 454.44, median: 578.31, p75: 627.5, p90: 663.64, max: 1154.16, spreadMultiple: 5.1, p90ToP10: 1.79 },
    lowerQuartileGap: 123.87,
    listedRates: [],
    matchedProviders: 0,
    highlights: [],
    notes: [],
    ...overrides,
  }
}

const groups = [
  makeGroup('asc'),
  makeGroup('office', {
    feeType: 'professional',
    feeTypeLabel: 'Professional (clinician) fee',
    settingLabel: "Doctor's office",
    providerCount: 3,
    statsLevel: 'list',
    stats: { min: 300, p10: null, p25: null, median: null, p75: null, p90: null, max: 450, spreadMultiple: null, p90ToP10: null },
    lowerQuartileGap: null,
  }),
]

function makeRow(name: string, groupKey: string, rate: number, band: RateRow['band']): RateRow {
  return {
    groupKey,
    providerKey: name,
    ein: '113466729',
    einDisplay: '11-3466729',
    einMasked: false,
    einLevel: false,
    businessName: name,
    otherNames: [],
    rate,
    rateLow: rate,
    rateHigh: rate,
    multipleRates: false,
    vsMedian: null,
    percentile: null,
    band,
    npiCount: 1,
    occurrences: 1,
    rateCount: 1,
    firstRateId: 1,
    matched: false,
    rateIds: [1],
    wholeTaxIdRateId: null,
  }
}

const rows = [
  makeRow('Low Cost ASC', 'asc', 454.44, 'low'),
  makeRow('Typical ASC', 'asc', 578.31, 'typical'),
  makeRow('Middle ASC', 'asc', 610, 'typical'),
  makeRow('Pricey ASC', 'asc', 1405.33, 'high'),
  makeRow('Small Practice', 'office', 300, null),
]

function mountTable() {
  return mount(ProviderTable, {
    props: { rows: { total: rows.length, page: 1, pageSize: 25, items: rows }, groups, sort: 'rate' },
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } })] },
  })
}

describe('ProviderTable', () => {
  it('compares each price with typical in words and dollars, with no statistics words', () => {
    const wrapper = mountTable()
    const headers = wrapper.findAll('th').map((th) => th.text())
    expect(headers).toContain('Compared with typical')
    expect(headers.join(' ')).not.toMatch(/median|percentile|position/i)

    const compared = wrapper.findAll('[data-testid="row-compared"]').map((cell) => [
      cell.find('[data-testid="band-chip"]').exists() ? cell.get('[data-testid="band-chip"]').text() : cell.text(),
      cell.find('[data-testid="row-difference"]').exists() ? cell.get('[data-testid="row-difference"]').text() : null,
    ])
    expect(compared).toEqual([
      // Against the "$578" the card shows: $454.44 is $123.56 less, $610 is $32 more.
      ['Lower than most', '$123.56 less than typical'],
      ['About typical', null],
      // In the API's middle band but over 5% above typical: the dollars, not "About typical".
      ['$32 more than typical', '$32 more than typical'],
      ['Higher than most', 'About 2.4 times the typical price'],
      ['Too few to compare', null],
    ])
    expect(wrapper.text()).not.toMatch(/percentile|median|quartile|×/i)
  })

  it("names the fee in plain words: doctor's fee or facility fee", () => {
    const text = mountTable().text()
    expect(text).toContain('Facility fee · Ambulatory surgery center')
    expect(text).toContain("Doctor's fee · Doctor's office")
  })
})
