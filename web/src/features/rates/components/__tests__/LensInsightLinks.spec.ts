import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { RateGroup, RateSearchResult } from '../../api/types'
import { parseRouteQuery } from '../../lib/searchParams'
import LensPanel from '../LensPanel.vue'

// The lenses link to the insights pages; each link only shows when the result says the page has something for it.

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

function result(groups: RateGroup[], query: Partial<RateSearchResult['query']> = {}): RateSearchResult {
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
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

function mountPanel(res: RateSearchResult, query: Record<string, string>) {
  const empty = { template: '<div />' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: empty },
      { path: '/insights/gaps', name: 'gaps', component: empty },
      { path: '/insights/compare', name: 'compare', component: empty },
      { path: '/providers/:key', name: 'scorecard', component: empty },
    ],
  })
  return mount(LensPanel, {
    props: { result: res, params: parseRouteQuery({ code: '45378', ...query }) },
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router] },
  })
}

function link(wrapper: ReturnType<typeof mountPanel>, testId: string): { path: string; query: Record<string, string> } {
  const href = new URL(wrapper.get(`[data-testid="${testId}"]`).attributes('href')!, 'http://localhost')
  return { path: href.pathname, query: Object.fromEntries(href.searchParams) }
}

describe('lens links to the insights', () => {
  it("providers: an EIN that picked one provider links to its scorecard", () => {
    const highlights = [{ providerKey: '113471596', displayName: 'Minkowitz', rate: 41.5, percentile: 50, vsMedian: 1, aboveP25: 0, rateLow: 41.5, rateHigh: 41.5 }]
    const wrapper = mountPanel(result([group({ matchedProviders: 1, highlights })], { ein: '11-3471596' }), { lens: 'providers', ein: '11-3471596' })

    expect(link(wrapper, 'scorecard-link')).toEqual({ path: '/providers/113471596', query: {} })
    expect(wrapper.text()).not.toContain("not only the NPI's own")
  })

  it('providers: an NPI says the scorecard covers its whole tax ID; a name filter gets no link', () => {
    const highlights = [{ providerKey: '113471596', displayName: 'Minkowitz', rate: 41.5, percentile: 50, vsMedian: 1, aboveP25: 0, rateLow: 41.5, rateHigh: 41.5 }]
    const byNpi = mountPanel(result([group({ matchedProviders: 1, highlights })], { npi: '1396747861' }), { lens: 'providers', npi: '1396747861' })
    const byName = mountPanel(result([group({ matchedProviders: 1, highlights })], { provider: 'mink' }), { lens: 'providers', provider: 'mink' })

    expect(byNpi.text()).toContain("It covers every rate under the NPI's tax ID, not only the NPI's own.")
    expect(byName.find('[data-testid="scorecard-link"]').exists()).toBe(false)
  })

  it('plans: ranks the code by spread and compares the two products when both have the group', () => {
    const essential = group({ key: 'essential|CPT|45378|facility|asc||negotiated', productKey: 'essential', productLabel: 'Essential Plan' })
    const wrapper = mountPanel(result([group(), essential]), { lens: 'plans' })

    expect(link(wrapper, 'lens-gaps-link')).toEqual({ path: '/insights/gaps', query: { code: '45378', sort: 'range' } })
    expect(link(wrapper, 'lens-compare-link')).toEqual({ path: '/insights/compare', query: { code: '45378' } })
    expect(wrapper.get('[data-testid="lens-gaps-link"]').text()).toBe('See how widely prices range for this code')
  })

  it('plans: no comparison link with one product; the whole leaderboard when no group is ranked', () => {
    const wrapper = mountPanel(result([group({ statsLevel: 'basic', stats: { ...group().stats, p10: null, p90: null, p90ToP10: null } })]), { lens: 'plans' })

    expect(wrapper.find('[data-testid="lens-compare-link"]').exists()).toBe(false)
    expect(link(wrapper, 'lens-gaps-link')).toEqual({ path: '/insights/gaps', query: { sort: 'range' } })
    expect(wrapper.get('[data-testid="lens-gaps-link"]').text()).toBe('See where prices range widest')
  })

  it("employers: ranks the code's groups by gap under the search's filters", () => {
    const wrapper = mountPanel(result([group()], { product: 'ambetter', feeType: 'facility' }), { lens: 'employers', product: 'ambetter', feeType: 'facility' })

    expect(link(wrapper, 'lens-gaps-link')).toEqual({ path: '/insights/gaps', query: { code: '45378', product: 'ambetter', feeType: 'facility' } })
  })
})
