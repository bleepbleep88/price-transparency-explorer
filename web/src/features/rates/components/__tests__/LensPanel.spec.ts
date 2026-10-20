import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { money } from '@/lib/format'
import type { CodeInfo, RateGroup, RateSearchResult } from '../../api/types'
import { parseRouteQuery } from '../../lib/searchParams'
import LensPanel from '../LensPanel.vue'

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
    highlights: [],
    notes: [],
    ...overrides,
  }
}

function result(groups: RateGroup[], query: Partial<RateSearchResult['query']> = {}, code: CodeInfo | null = null): RateSearchResult {
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
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 13789, uniqueRates: 77, duplicatesCollapsed: 13712, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

function mountPanel(res: RateSearchResult, query: Record<string, string>) {
  const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
  return mount(LensPanel, { props: { result: res, params: parseRouteQuery({ code: '45378', ...query }) }, global: { plugins: [vuetify] } })
}

describe('LensPanel', () => {
  it('is a compact picker until a lens is chosen, and choosing keeps the page', async () => {
    const wrapper = mountPanel(result([group()]), { page: '2' })
    expect(wrapper.text()).toContain('What does this mean for…')
    expect(wrapper.find('[data-testid="lens-employers"]').exists()).toBe(false)

    await wrapper.get('[data-testid="lens-pick-plans"]').trigger('click')
    expect(wrapper.emitted('update')).toEqual([[{ lens: 'plans', page: 2 }]])
  })

  it('clears the lens when the active one is picked again', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'plans' })
    expect(wrapper.text()).toContain('What this means for you')
    await wrapper.get('[data-testid="lens-pick-plans"]').trigger('click')
    expect(wrapper.emitted('update')).toEqual([[{ lens: null, page: 1 }]])
  })

  it('providers: says where your rate sits, or asks for an NPI', async () => {
    const office = group({
      highlights: [{ providerKey: '995084138', displayName: 'Heel Sole Podiatry PC', rate: 80.72, percentile: 68, vsMedian: 1.57, aboveP25: 29.27 }],
    })
    const withNpi = mountPanel(result([office], { npi: '1801474036' }), { lens: 'providers', npi: '1801474036' })
    expect(withNpi.get('[data-testid="your-rate-line"]').text()).toContain('Your rate $80.72 is at the 68th percentile, 1.57× the median.')

    const without = mountPanel(result([group()]), { lens: 'providers' })
    await without.get('[data-testid="focus-npi"]').trigger('click')
    expect(without.emitted('focusNpi')).toHaveLength(1)
  })

  it('plans: widest spread first, and one click sorts by the highest rate', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'plans' })
    expect(wrapper.get('[data-testid="spread-line"]').text()).toContain('the median sits $123.86 above the lower quartile')
    await wrapper.get('[data-testid="sort-highest"]').trigger('click')
    expect(wrapper.emitted('update')).toEqual([[{ sort: '-rate', page: 1 }]])
  })

  it('employers: typical cost and gap, and cases a year become a yearly figure', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'employers', cases: '1200' })
    expect(wrapper.get('[data-testid="cost-line"]').text()).toContain(money(578.31))
    expect(wrapper.get('[data-testid="yearly-gap"]').text()).toContain(money(123.86 * 1200))
    expect(wrapper.text()).toContain("Negotiated rates aren't what members pay")

    // A bigger fee-schedule group with no gap doesn't make the sentence say "$0.00 a year".
    const feeSchedule = group({ key: 'fee-schedule', providerCount: 51, lowerQuartileGap: 0 })
    const mixed = mountPanel(result([feeSchedule, group()]), { lens: 'employers', cases: '1200' })
    expect(mixed.get('[data-testid="yearly-gap"]').text()).toContain(money(123.86 * 1200))

    await wrapper.get('[data-testid="cases-input"] input').setValue('300')
    const events = wrapper.emitted('cases') ?? []
    expect(events[events.length - 1]).toEqual([300])
  })

  it('innovators: the clean-up numbers, the exact API request, and the API reference', () => {
    const wrapper = mountPanel(result([group()]), { lens: 'innovators' })
    expect(wrapper.text()).toContain('13,789 published rows became 77 unique rates.')
    expect(wrapper.get('[data-testid="api-url"]').text()).toBe(
      `${window.location.origin}/api/v1/rates?code=45378&sort=rate&page=1&pageSize=25`,
    )
    const hrefs = wrapper.findAll('a').map((link) => link.attributes('href'))
    expect(hrefs).toEqual(expect.arrayContaining(['/scalar/v1', '/openapi/v1.json']))
  })

  it('life sciences: unit prices by site for a drug, the facility-charge note, examples for anything else', async () => {
    const drug: CodeInfo = {
      codeType: 'HCPCS',
      code: 'J1100',
      name: 'INJ DEXMETH SODM PHOSHA 1MG',
      description: 'Injection, dexamethasone sodium phosphate, 1 mg',
      descriptionSource: 'payer_file',
      category: { key: 'drugs', label: 'Drugs & biologicals', unit: 'per HCPCS unit', perUnit: true },
      levelFamily: null,
      foundVia: [],
    }
    const office = group({
      key: 'j1100-office',
      codeType: 'HCPCS',
      code: 'J1100',
      feeTypeLabel: 'Professional (clinician) fee',
      settingLabel: "Doctor's office",
      modifiers: [{ code: 'JA', label: 'Administered intravenously' }],
      perUnit: true,
      unit: "per HCPCS unit (see the code's description)",
      billedAs: [{ billingCodeType: 'HCPCS', billingCode: 'J1100', label: 'HCPCS J1100' }],
      providerCount: 50,
      stats: { min: 0.11, p10: 0.11, p25: 0.11, median: 0.11, p75: 0.11, p90: 0.11, max: 0.11, spreadMultiple: 1, p90ToP10: 1 },
      lowerQuartileGap: 0,
    })
    const facilityCharge = group({
      key: 'j1100-asc',
      codeType: 'HCPCS',
      code: 'J1100',
      perUnit: true,
      billedAs: [{ billingCodeType: 'RC', billingCode: '0490', label: 'Revenue code 0490 (Ambulatory surgery)' }],
    })
    const wrapper = mountPanel(result([office, facilityCharge], { code: 'J1100' }, drug), { lens: 'life-sciences' })
    const rows = wrapper.findAll('[data-testid="unit-price-line"]')
    expect(rows).toHaveLength(1)
    expect(rows[0]!.text()).toContain('$0.11 median')
    expect(wrapper.text()).toContain('One unit, as the code defines it: Injection, dexamethasone sodium phosphate, 1 mg.')
    expect(wrapper.get('[data-testid="facility-charges-note"]').text()).toContain("aren't per-unit prices")

    const colonoscopy: CodeInfo = { ...drug, codeType: 'CPT', code: '45378', category: { key: 'surgery', label: 'Surgery & procedures', unit: 'per procedure', perUnit: false } }
    const other = mountPanel(result([group()], {}, colonoscopy), { lens: 'life-sciences' })
    expect(other.text()).toContain("This isn't a drug code.")
    await other.get('[data-testid="drug-example-J0585"]').trigger('click')
    expect(other.emitted('search')).toEqual([['J0585']])
  })
})
