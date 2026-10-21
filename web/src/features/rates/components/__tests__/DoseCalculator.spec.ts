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

function mountCalculator(dose?: number, code: CodeInfo = propofol, groups: RateGroup[] = [office], focus: { plan?: string; group?: string } = {}) {
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
  return mount(DoseCalculator, { props: { result, dose, ...focus }, global: { plugins: [createVuetify()] } })
}

// The main view: a dose box and one answer line, a handful of words. Everything else is in Details.
const WORDS_MAX = 25
const words = (text: string) => text.split(/\s+/).filter(Boolean).length

describe('DoseCalculator', () => {
  it('answers a dose in one line, keeps it in the URL, and puts the rest in Details', async () => {
    const wrapper = mountCalculator()
    expect(wrapper.text()).toContain('Price per dose')
    expect(wrapper.get('[data-testid="dose-answer"]').attributes('aria-live')).toBe('polite')

    await wrapper.get('[data-testid="dose-input"] input').setValue('125')
    expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('125 mg = 13 units → $1.17')
    expect(wrapper.get('[data-testid="dose-unit"]').text()).toBe('1 unit = 10 mg')
    const doses = wrapper.emitted('dose') ?? []
    expect(doses[doses.length - 1]).toEqual([125])

    // Details: closed until asked; it holds the card, the typical price and the notes.
    const main = wrapper.text().replace(wrapper.get('[data-testid="dose-details"]').text(), '')
    expect(words(main)).toBeLessThanOrEqual(WORDS_MAX)
    expect(main).not.toMatch(/modifier|JW|HCPCS|J2704/)
    await wrapper.get('[data-testid="dose-details-toggle"]').trigger('click')
    const details = wrapper.get('[data-testid="dose-details"]').text()
    expect(details).toContain("Price from: Essential Plan · Doctor · in the office")
    expect(details).toContain('Typical price: $0.09 a unit')
    expect(details).toContain('modifier JW')
  })

  it('reads the dose from the URL; a rejected entry says so in a few words and drops the dose from the URL', async () => {
    const wrapper = mountCalculator(150)
    expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('150 mg = 15 units → $1.35')

    for (const text of ['-3', '0x10', '1e3']) {
      await wrapper.get('[data-testid="dose-input"] input').setValue(text)
      expect(wrapper.text()).toContain('Enter a number, like 45.')
      expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('')
      const doses = wrapper.emitted('dose') ?? []
      expect(doses[doses.length - 1]).toEqual([null])
    }
    await wrapper.get('[data-testid="dose-input"] input').setValue('2,000,000')
    expect(wrapper.text()).toContain('Too big: over 100,000 units.')
  })

  it('takes a dose of units as units, with no "1 unit =" line', async () => {
    const botox: CodeInfo = { ...propofol, code: 'J0585', doseUnit: { amount: 1, unit: 'unit', text: '1 unit' } }
    const wrapper = mountCalculator(100, botox)
    expect(wrapper.get('[data-testid="dose-input"]').text()).toContain('units')
    expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('100 units → $9')
    expect(wrapper.find('[data-testid="dose-unit"]').exists()).toBe(false)
  })

  it("won't price a dose from discarded drug (JW), and says so in one short line", () => {
    const discarded = { ...office, key: 'jw', modifiers: [{ code: 'JW', label: 'Drug amount discarded' }] }
    const wrapper = mountCalculator(100, propofol, [discarded])
    expect(wrapper.get('[data-testid="dose-reason"]').text()).toBe('Only priced for thrown-away drug. No dose price.')
    expect(wrapper.find('[data-testid="dose-input"]').exists()).toBe(false)
    expect(wrapper.text()).not.toMatch(/\$\d/)
  })

  it('says when every card has too few places', () => {
    const wrapper = mountCalculator(200, propofol, [{ ...office, providerCount: 3, statsLevel: 'list' }])
    expect(wrapper.get('[data-testid="dose-reason"]').text()).toBe('Too few prices to work out a dose.')
  })

  it("uses the plan tab's price, and follows the tab (J1100: Essential $0.11, Ambetter $0.09)", async () => {
    const dexamethasone: CodeInfo = { ...propofol, code: 'J1100', doseUnit: { amount: 1, unit: 'mg', text: '1 mg' } }
    const plan = (key: string, label: string, median: number, providers: number): RateGroup => ({
      ...office,
      key: `${key}|HCPCS|J1100|professional|office||negotiated`,
      productKey: key,
      productLabel: label,
      code: 'J1100',
      providerCount: providers,
      stats: { ...office.stats, median },
    })
    const groups = [plan('ambetter-from-fidelis-care', 'Ambetter from Fidelis Care', 0.09, 40), plan('essential-plan', 'Essential Plan', 0.11, 12)]
    const wrapper = mountCalculator(8, dexamethasone, groups, { plan: 'essential-plan', group: groups[1]!.key })
    expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('8 mg = 8 units → $0.88')
    expect(wrapper.get('[data-testid="dose-card-label"]').text()).toContain('Essential Plan')

    await wrapper.setProps({ plan: 'ambetter-from-fidelis-care', group: groups[0]!.key })
    expect(wrapper.get('[data-testid="dose-answer"]').text()).toBe('8 mg = 8 units → $0.72')
  })

  it('labels its Details button for the dose', () => {
    expect(mountCalculator(45).get('[data-testid="dose-details-toggle"]').text()).toBe('Details: dose')
  })

  it('stays hidden for a code whose name states no unit', () => {
    expect(mountCalculator(undefined, { ...propofol, doseUnit: null }).find('[data-testid="dose-calculator"]').exists()).toBe(false)
  })
})
