import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { CodeInfo, LocationOption, RateSearchResult } from '../../api/types'
import { parseRouteQuery } from '../../lib/searchParams'
import ResultSummary from '../ResultSummary.vue'

const locations: LocationOption[] = [
  { label: 'Brooklyn', isBorough: true, npis: 4874, county: 'Kings County' },
  { label: 'Delaware', isBorough: false, npis: 61, county: 'Delaware County' },
]

function result(query: Partial<RateSearchResult['query']>): RateSearchResult {
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
      specialty: null,
      county: null,
      countyPlace: null,
      group: null,
      sort: 'rate',
      page: 1,
      pageSize: 25,
      ...query,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code: null,
    groups: [],
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

function chips(res: RateSearchResult, query: Record<string, string>) {
  const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
  const wrapper = mount(ResultSummary, {
    props: { result: res, params: parseRouteQuery({ code: '45378', ...query }), products: [], settings: [], locations },
    global: { plugins: [vuetify] },
  })
  return wrapper.findAll('[data-testid="active-filter"]')
}

describe('ResultSummary', () => {
  it('names the place the location matched, as the API does', () => {
    const [kings] = chips(result({ county: 'Kings County', countyPlace: 'Brooklyn' }), { county: 'Kings County' })
    expect(kings!.text()).toBe('Location: Brooklyn')
    const [delaware] = chips(result({ county: 'Delaware', countyPlace: 'Delaware County' }), { county: 'Delaware' })
    expect(delaware!.text()).toBe('Location: Delaware County')
  })

  it('marks only an NPI, EIN or name chip as the provider searched', () => {
    const all = chips(result({ npi: '1174873780', specialty: 'gastro', county: 'Brooklyn', countyPlace: 'Brooklyn' }), {
      npi: '1174873780',
      specialty: 'gastro',
      county: 'Brooklyn',
    })
    expect(all.map((chip) => chip.classes().includes('copper-mark'))).toEqual([true, false, false])
    // Long specialty names wrap inside the chip, so its remove button stays in view.
    expect(all.every((chip) => chip.classes().includes('active-filter'))).toBe(true)
  })

  it('shows the CMS category, a name from CMS when the plan has none, and how it is priced', async () => {
    const propofol: CodeInfo = {
      codeType: 'HCPCS',
      code: 'J2704',
      name: 'Injection, propofol, 10 mg',
      description: null,
      descriptionSource: 'cms_hcpcs',
      category: { key: 'drugs', label: 'Drugs & biologicals', unit: "per HCPCS unit (see the code's description)", perUnit: true },
      classification: { key: 'RI', category: 'Treatments', subcategory: 'Injections and Infusions (nononcologic)', family: null, source: 'cms_rbcs' },
      doseUnit: { amount: 10, unit: 'mg', text: '10 mg' },
      levelFamily: null,
      foundVia: ['Billing code (HCPCS)'],
    }
    const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
    const wrapper = mount(ResultSummary, {
      props: { result: { ...result({ code: 'J2704' }), code: propofol }, params: parseRouteQuery({ code: 'J2704' }), products: [], settings: [], locations },
      global: { plugins: [vuetify] },
    })

    expect(wrapper.get('[data-testid="category-chip"]').text()).toBe('Treatments · Injections and Infusions (nononcologic)')
    expect(wrapper.get('[data-testid="code-name"]').text()).toBe('Injection, propofol, 10 mg')
    expect(wrapper.get('[data-testid="cms-name"]').text()).toContain("Name from CMS's HCPCS list")
    await wrapper.get('[data-testid="pricing-notes"] button').trigger('click')
    expect(wrapper.get('[data-testid="pricing-note-drugs"]').text()).toContain('(for J2704, 10 mg)')
  })

  it('names a code the plan describes without the CMS note', () => {
    const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
    const colonoscopy: CodeInfo = {
      codeType: 'CPT',
      code: '45378',
      name: 'DIAGNOSTIC COLONOSCOPY',
      description: null,
      descriptionSource: 'payer_file',
      category: { key: 'surgery', label: 'Surgery & procedures', unit: 'per procedure', perUnit: false },
      classification: { key: 'PG004', category: 'Procedures', subcategory: 'Digestive/Gastrointestinal', family: 'Lower GI Endoscopy - Other', source: 'cms_rbcs' },
      doseUnit: null,
      levelFamily: null,
      foundVia: [],
    }
    const wrapper = mount(ResultSummary, {
      props: { result: { ...result({}), code: colonoscopy }, params: parseRouteQuery({ code: '45378' }), products: [], settings: [], locations },
      global: { plugins: [vuetify] },
    })

    expect(wrapper.get('[data-testid="category-chip"]').text()).toBe('Procedures · Digestive/Gastrointestinal')
    expect(wrapper.find('[data-testid="cms-name"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="pricing-notes"]').exists()).toBe(false)
  })
})
