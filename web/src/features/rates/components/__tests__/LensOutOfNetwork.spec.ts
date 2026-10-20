import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { OutOfNetworkResult } from '@/features/out-of-network/api/types'
import { LISTED_CT_READ, oonGroup, oonResult } from '@/features/out-of-network/lib/__tests__/fixtures'
import type { RateGroup, RateSearchResult } from '../../api/types'
import { parseRouteQuery } from '../../lib/searchParams'
import LensPanel from '../LensPanel.vue'

// The lenses read the out-of-network answer the page already has: one line in the plans and providers lenses, and a link
// to the sources page in the innovators lens.

function rateGroup(): RateGroup {
  return {
    key: 'essential-plan|RC|0450|facility|er||negotiated',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'RC',
    code: '0450',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'er',
    settingLabel: 'Hospital emergency room',
    settingNote: null,
    places: [],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per facility charge (unit not stated in the file)',
    perUnit: false,
    billedAs: [],
    providerCount: 77,
    rateCount: 90,
    statsLevel: 'full',
    stats: { min: 120, p10: 250, p25: 330, median: 404.74, p75: 520, p90: 700, max: 1500, spreadMultiple: 12.5, p90ToP10: 2.8 },
    lowerQuartileGap: 74.74,
    listedRates: [],
    matchedProviders: 0,
    highlights: [],
    notes: [],
  }
}

function searchResult(): RateSearchResult {
  return {
    query: { code: '0450', matchedCodes: ['0450', '00450'], codeType: null, npi: null, ein: null, product: null, setting: null, feeType: null, provider: null, group: null, sort: 'rate', page: 1, pageSize: 25 },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code: null,
    groups: [rateGroup()],
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 2000, uniqueRates: 100, duplicatesCollapsed: 1900, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

async function mountLens(lens: string, outOfNetwork: OutOfNetworkResult | null, query: Record<string, string> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: { template: '<div />' } },
      { path: '/sources', name: 'sources', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(LensPanel, {
    props: { result: searchResult(), params: parseRouteQuery({ code: '0450', lens, ...query }), outOfNetwork },
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router] },
  })
  await flushPromises()
  return wrapper
}

describe('lenses and out-of-network payments', () => {
  it('plans: one line on what the plan allowed out of network, as a share of the charge', async () => {
    const wrapper = await mountLens('plans', oonResult([oonGroup(), LISTED_CT_READ]))

    expect(wrapper.get('[data-testid="lens-oon-line"]').text()).toBe(
      'Out of network (Essential Plan · Facility fee · RC 0450), the plan allowed a median 21% of what providers billed, over 20 payments.',
    )
  })

  it('providers: one line on what providers outside the network were allowed, with or without an NPI', async () => {
    const without = await mountLens('providers', oonResult([oonGroup()]))
    const withNpi = await mountLens('providers', oonResult([oonGroup()]), { npi: '1801474036' })

    const line = 'Providers outside the network were allowed a median 21% of what they billed (Essential Plan · Facility fee, 20 payments).'
    expect(without.get('[data-testid="lens-oon-line"]').text()).toBe(line)
    expect(withNpi.get('[data-testid="lens-oon-line"]').text()).toBe(line)
  })

  it('no out-of-network payments, or none under the product filter: no line', async () => {
    const none = await mountLens('plans', oonResult([]))
    const filtered = await mountLens('plans', oonResult([oonGroup()]), { product: 'ambetter-from-fidelis-care' })
    const notLoaded = await mountLens('providers', null)

    expect(none.find('[data-testid="lens-oon-line"]').exists()).toBe(false)
    expect(filtered.find('[data-testid="lens-oon-line"]').exists()).toBe(false)
    expect(notLoaded.find('[data-testid="lens-oon-line"]').exists()).toBe(false)
  })

  it('innovators: links to the sources page', async () => {
    const wrapper = await mountLens('innovators', null)

    const link = wrapper.get('[data-testid="lens-sources-link"]')
    expect(link.text()).toBe('Sources and data quality')
    expect(link.attributes('href')).toBe('/sources')
  })
})
