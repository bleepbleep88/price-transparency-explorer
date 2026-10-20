import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { CodeInfo, RateGroup, RateSearchResult } from '../../api/types'
import DoseCalculator from '../DoseCalculator.vue'

const propofol: CodeInfo = {
  codeType: 'HCPCS',
  code: 'J2704',
  name: 'INJECTION, PROPOFOL, 10 MG',
  description: null,
  descriptionSource: 'payer_file',
  category: { key: 'drugs', label: 'Drugs & biologicals', unit: 'per HCPCS unit', perUnit: true },
  classification: { key: 'RI', category: 'Treatments', subcategory: 'Injections and Infusions (nononcologic)', family: null, source: 'cms_rbcs' },
  doseUnit: { amount: 10, unit: 'mg', text: '10 mg' },
  levelFamily: null,
  foundVia: [],
}

const office: RateGroup = {
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
  providerCount: 6,
  rateCount: 6,
  statsLevel: 'basic',
  stats: { min: 0.05, p10: null, p25: null, median: 0.09, p75: null, p90: null, max: 0.2, spreadMultiple: null, p90ToP10: null },
  lowerQuartileGap: null,
  listedRates: [],
  matchedProviders: 0,
  highlights: [],
  notes: [],
}

function mountCalculator(dose?: number, code: CodeInfo = propofol, groups: RateGroup[] = [office]) {
  const result: RateSearchResult = {
    query: {
      code: 'J2704',
      matchedCodes: [],
      codeType: null,
      npi: null,
      ein: null,
      product: null,
      setting: null,
      feeType: null,
      provider: null,
      specialty: null,
      county: null,
      countyPlace: null,
      group: null,
      sort: 'rate',
      page: 1,
      pageSize: 25,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
  return mount(DoseCalculator, { props: { result, dose }, global: { plugins: [createVuetify()] } })
}

describe('DoseCalculator', () => {
  it('turns a dose into whole units and a cost at the typical price, and keeps the dose in the URL', async () => {
    const wrapper = mountCalculator()
    expect(wrapper.text()).toContain("What a dose comes to at the plan's rates")
    expect(wrapper.text()).toContain('One unit of J2704 is 10 mg')
    expect(wrapper.get('[data-testid="dose-math-region"]').attributes('aria-live')).toBe('polite')

    await wrapper.get('[data-testid="dose-input"] input').setValue('125')
    const math = wrapper.get('[data-testid="dose-math"]').text()
    expect(math).toContain("125 mg divided by 10 mg is 12.5. Units are billed whole, so that's 13 units.")
    expect(math).toContain('At the typical price of $0.09 a unit, 13 units come to $1.17.')
    const doses = wrapper.emitted('dose') ?? []
    expect(doses[doses.length - 1]).toEqual([125])
    expect(wrapper.get('[data-testid="dose-notes"]').text()).toContain('modifier JW')
  })

  it('reads the dose from the URL; a rejected entry says so and drops the dose from the URL', async () => {
    const wrapper = mountCalculator(150)
    expect(wrapper.get('[data-testid="dose-math"]').text()).toContain('150 mg divided by 10 mg is 15 units.')

    for (const text of ['-3', '0x10', '1e3']) {
      await wrapper.get('[data-testid="dose-input"] input').setValue(text)
      expect(wrapper.text()).toContain('Enter a dose as a plain number')
      expect(wrapper.find('[data-testid="dose-math"]').exists()).toBe(false)
      const doses = wrapper.emitted('dose') ?? []
      expect(doses[doses.length - 1]).toEqual([null])
    }
    await wrapper.get('[data-testid="dose-input"] input').setValue('2,000,000')
    expect(wrapper.text()).toContain("That's more than 100,000 units")
  })

  it('labels a dose of units "units"', () => {
    const botox: CodeInfo = { ...propofol, code: 'J0585', doseUnit: { amount: 1, unit: 'unit', text: '1 unit' } }
    expect(mountCalculator(undefined, botox).get('[data-testid="dose-input"]').text()).toContain('Dose (units)')
  })

  it("won't price a dose from discarded drug (JW), and says why", () => {
    const discarded = { ...office, key: 'jw', modifiers: [{ code: 'JW', label: 'Drug amount discarded' }] }
    const wrapper = mountCalculator(100, propofol, [discarded])
    expect(wrapper.get('[data-testid="dose-reason"]').text()).toBe(
      "This plan lists a price with 5 or more providers only for discarded drug (modifier JW), so we can't price a dose.",
    )
    expect(wrapper.find('[data-testid="dose-input"]').exists()).toBe(false)
    expect(wrapper.text()).not.toMatch(/\$\d/)
  })

  it('says when every card has too few providers', () => {
    const wrapper = mountCalculator(200, propofol, [{ ...office, providerCount: 3, statsLevel: 'list' }])
    expect(wrapper.get('[data-testid="dose-reason"]').text()).toBe('Too few providers to price a dose: each card has fewer than 5.')
  })

  it('stays hidden for a code whose name states no unit', () => {
    expect(mountCalculator(undefined, { ...propofol, doseUnit: null }).find('[data-testid="dose-calculator"]').exists()).toBe(false)
  })
})
