import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
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
    matchedProviders: 0,
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

const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|index|comparison group|≈/i

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

  it('exposes the picker as a named group of toggle buttons', () => {
    const wrapper = mountPanel(result([group()]), { lens: 'plans' })
    const picker = wrapper.get('[role="group"]')
    expect(picker.attributes('aria-label')).toBe('Audience')
    expect(wrapper.get('[data-testid="lens-pick-plans"]').attributes()).toMatchObject({ role: 'button', 'aria-pressed': 'true' })
    expect(wrapper.get('[data-testid="lens-pick-employers"]').attributes('aria-pressed')).toBe('false')
  })

  it('clears the lens when the active one is picked again', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'plans' })
    expect(wrapper.text()).toContain('What this means for you')
    await wrapper.get('[data-testid="lens-pick-plans"]').trigger('click')
    expect(wrapper.emitted('update')).toEqual([[{ lens: null, page: 1 }]])
  })

  it('providers: says how your price compares, or asks for an NPI', async () => {
    const office = group({
      highlights: [{ providerKey: '995084138', displayName: 'Heel Sole Podiatry PC', rate: 80.72, percentile: 68, vsMedian: 1.57, aboveP25: 29.27, rateLow: 80.72, rateHigh: 80.72 }],
    })
    const withNpi = mountPanel(result([office], { npi: '1801474036' }), { lens: 'providers', npi: '1801474036' })
    expect(withNpi.get('[data-testid="your-rate-line"]').text()).toContain(
      'Your price $80.72: lower than most, $498 less than typical. Typical price: $578.',
    )

    const without = mountPanel(result([group()]), { lens: 'providers' })
    await without.get('[data-testid="focus-npi"]').trigger('click')
    expect(without.emitted('focusNpi')).toHaveLength(1)
  })

  it('providers: a name search names the filter and counts the cards left, not the lines', () => {
    const matches = (prefix: string) =>
      Array.from({ length: 4 }, (_, i) => ({ providerKey: `${prefix}${i}`, displayName: `Gastro Practice ${prefix}${i}`, rate: 300 + i, percentile: 40 + i, vsMedian: 1.1, aboveP25: 10, rateLow: 300 + i, rateHigh: 300 + i }))
    const groups = ['a', 'b', 'c'].map((key) => group({ key, highlights: matches(key) }))
    const wrapper = mountPanel(result(groups, { provider: 'gastro' }), { lens: 'providers', provider: 'gastro' })
    expect(wrapper.text()).toContain('Providers whose name contains "gastro":')
    expect(wrapper.text()).not.toContain('your provider')
    expect(wrapper.text()).not.toContain('Your price')
    expect(wrapper.findAll('[data-testid="your-rate-line"]')).toHaveLength(8)
    expect(wrapper.get('[data-testid="more-groups"]').text()).toBe('1 more card below.')
  })

  it('providers: says when a group lists only its first matches', () => {
    const highlights = Array.from({ length: 5 }, (_, i) => ({
      providerKey: `p${i}`,
      displayName: `Medical Practice ${i}`,
      rate: 100 + i,
      percentile: 30 + i,
      vsMedian: 0.9,
      aboveP25: 5,
      rateLow: 100 + i,
      rateHigh: 100 + i,
    }))
    const wrapper = mountPanel(result([group({ matchedProviders: 12, highlights })], { provider: 'medical' }), { lens: 'providers', provider: 'medical' })
    expect(wrapper.findAll('[data-testid="your-rate-line"]')).toHaveLength(5)
    expect(wrapper.get('[data-testid="highlight-cap"]').text()).toBe('Showing 5 of 12 matching providers; the table lists them all.')
  })

  it('plans: where prices vary most first, and one click shows the highest prices for it', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'plans' })
    const line = wrapper.get('[data-testid="spread-line"]').text()
    expect(line).toContain('31 providers · Typical price: $578')
    expect(line).toContain('Even leaving out the extremes, the high end is about 1.8 times the low end. $124 less per case at a lower-priced provider.')
    await wrapper.get('[data-testid="show-highest"]').trigger('click')
    expect(wrapper.emitted('update')).toEqual([[{ group: 'ambetter|CPT|45378|facility|asc||negotiated', sort: '-rate', page: 1 }]])
  })

  it('plans: says so when prices barely vary, or when no card has the 10 providers it takes', () => {
    const flat = group({ stats: { min: 7.94, p10: 7.94, p25: 7.94, median: 7.94, p75: 7.94, p90: 7.94, max: 7.94, spreadMultiple: 1, p90ToP10: 1 }, lowerQuartileGap: 0 })
    const wrapper = mountPanel(result([flat]), { lens: 'plans' })
    expect(wrapper.text()).toContain('Prices barely vary here: on each card, most providers get about the same price.')
    expect(wrapper.find('[data-testid="spread-line"]').exists()).toBe(false)

    const small = group({ providerCount: 8, statsLevel: 'basic', stats: { ...group().stats, p10: null, p90: null }, lowerQuartileGap: null })
    expect(mountPanel(result([small]), { lens: 'plans' }).text()).toContain('It takes 10 providers to say how much prices vary')
  })

  it('employers: the typical price, how much less per case, and a yearly figure from your own case count', async () => {
    const wrapper = mountPanel(result([group()]), { lens: 'employers', cases: '1200' })
    const costLine = wrapper.get('[data-testid="cost-line"]').text()
    expect(costLine).toContain('$578')
    expect(costLine).toContain('$124 less per case')
    const yearly = wrapper.get('[data-testid="yearly-gap"]').text()
    expect(yearly).toContain('About $149,000 less a year for your 1,200 cases at lower-priced providers.')
    expect(yearly).toContain('Ambetter from Fidelis Care · Facility fee · Ambulatory surgery center')
    expect(wrapper.text()).toContain('These are individual-market plans (Ambetter, Essential Plan), not employer plans')
    expect(wrapper.text()).not.toMatch(BANNED)

    // A bigger fee-schedule group with no gap doesn't make the sentence say "$0 a year".
    const feeSchedule = group({ key: 'fee-schedule', providerCount: 51, lowerQuartileGap: 0 })
    const mixed = mountPanel(result([feeSchedule, group()]), { lens: 'employers', cases: '1200' })
    expect(mixed.get('[data-testid="yearly-gap"]').text()).toContain('About $149,000 less a year')

    // No case count, no yearly figure.
    expect(mountPanel(result([group()]), { lens: 'employers' }).find('[data-testid="yearly-gap"]').exists()).toBe(false)

    await wrapper.get('[data-testid="cases-input"] input').setValue('300')
    const events = wrapper.emitted('cases') ?? []
    expect(events[events.length - 1]).toEqual([300])
  })

  it('employers: the picked group leads the yearly sentence', () => {
    const essential = group({ key: 'essential-asc', productLabel: 'Essential Plan', providerCount: 27, lowerQuartileGap: 48 })
    const wrapper = mountPanel(result([group(), essential]), { lens: 'employers', cases: '12', group: 'essential-asc' })
    const yearly = wrapper.get('[data-testid="yearly-gap"]').text()
    expect(yearly).toContain('About $576 less a year for your 12 cases at lower-priced providers.')
    expect(yearly).toContain('Essential Plan')
  })

  it('employers: per-unit prices say so and get no yearly figure', () => {
    const anesthesia = group({ key: 'anesthesia', perUnit: true, unit: 'per anesthesia unit', providerCount: 13, lowerQuartileGap: 0.04 })
    const wrapper = mountPanel(result([anesthesia]), { lens: 'employers', cases: '100' })
    expect(wrapper.get('[data-testid="cost-line"]').text()).toContain('$578 per unit')
    expect(wrapper.get('[data-testid="cost-line"]').text()).toContain('$0.04 less per unit')
    expect(wrapper.get('[data-testid="per-unit-note"]').text()).toBe('Priced per unit; a case can use several units.')
    expect(wrapper.find('[data-testid="cases-input"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="yearly-gap"]').exists()).toBe(false)
  })

  it('innovators: the clean-up numbers, the exact API request, and the API reference', async () => {
    const writeText = vi.fn<(text: string) => Promise<void>>().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    const wrapper = mountPanel(result([group()]), { lens: 'innovators' })
    expect(wrapper.text()).toContain('In these results:')
    expect(wrapper.text()).toContain('13,789 published rows became 77 unique rates.')
    const url = `${window.location.origin}/api/v1/rates?code=45378&sort=rate&page=1&pageSize=25`
    expect(wrapper.get('[data-testid="api-url"]').text()).toBe(url)
    const hrefs = wrapper.findAll('a').map((link) => link.attributes('href'))
    expect(hrefs).toEqual(expect.arrayContaining(['/scalar/v1', '/openapi/v1.json']))

    // Copying confirms with a check and a live-region message, and nothing clears it until the URL changes.
    const status = wrapper.get('[data-testid="copy-status"]')
    expect(status.text()).toBe('')
    await wrapper.get('[data-testid="copy-api-url"]').trigger('click')
    await flushPromises()
    expect(writeText).toHaveBeenCalledWith(url)
    expect(status.text()).toBe('Copied the API request')
    expect(wrapper.get('[data-testid="copy-api-url"]').attributes('aria-label')).toBe('Copied the API request')
    await wrapper.setProps({ params: parseRouteQuery({ code: '45378', lens: 'innovators', page: '2' }) })
    expect(status.text()).toBe('')
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })
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
      feeType: 'professional',
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
    expect(rows[0]!.text()).toContain("Doctor's office · Doctor's fee")
    expect(rows[0]!.text()).toContain('Typical $0.11')
    expect(wrapper.text()).not.toMatch(BANNED)
    expect(wrapper.text()).toContain('One unit, as the code defines it: Injection, dexamethasone sodium phosphate, 1 mg.')
    expect(wrapper.get('[data-testid="facility-charges-note"]').text()).toContain("aren't per-unit prices")

    const colonoscopy: CodeInfo = { ...drug, codeType: 'CPT', code: '45378', category: { key: 'surgery', label: 'Surgery & procedures', unit: 'per procedure', perUnit: false } }
    const other = mountPanel(result([group()], {}, colonoscopy), { lens: 'life-sciences' })
    expect(other.text()).toContain("This isn't a drug code.")
    await other.get('[data-testid="drug-example-J0585"]').trigger('click')
    expect(other.emitted('search')).toEqual([['J0585']])
  })
})
