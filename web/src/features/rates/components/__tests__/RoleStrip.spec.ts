import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createRouter, createMemoryHistory } from 'vue-router'
import type { RateGroup, RateSearchResult } from '../../api/types'
import type { SearchParams } from '../../lib/searchParams'
import RoleStrip from '../RoleStrip.vue'

const center = {
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
  stats: { min: 247.85, p10: 389.37, p25: 450.92, median: 510.83, p75: 594.66, p90: 624.85, max: 688, spreadMultiple: 2.78, p90ToP10: 1.6 },
  lowerQuartileGap: 59.91,
  listedRates: [],
  highlights: [],
  matchedProviders: 0,
  notes: [],
} as unknown as RateGroup

function result(npi: string | null = null): RateSearchResult {
  const highlights = npi ? [{ providerKey: 'x', displayName: 'Goldstep', rate: 247.85, percentile: 0, vsMedian: 0.49, aboveP25: null, rateLow: 247.85, rateHigh: 247.85 }] : []
  return {
    query: {
      code: '45378', matchedCodes: ['45378'], codeType: null, npi, ein: null, product: null, setting: null, feeType: null,
      provider: null, specialty: null, county: null, countyPlace: null, group: null, sort: 'rate', page: 1, pageSize: 25,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-09-28', isLatest: true },
    code: null,
    groups: [{ ...center, highlights } as RateGroup],
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 1, uniqueRates: 1, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  } as RateSearchResult
}

function mountStrip(params: Partial<SearchParams>, npi: string | null = null) {
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/', component: { template: '<div />' } },
    { path: '/providers/:key', name: 'scorecard', component: { template: '<div />' } },
  ] })
  const searched = result(npi)
  return mount(RoleStrip, {
    props: { result: searched, params: { code: '45378', sort: 'rate', page: 1, pageSize: 25, ...params } as SearchParams, group: searched.groups[0]! },
    global: { plugins: [createVuetify(), router] },
  })
}

describe('RoleStrip, provider role', () => {
  it('asks for an NPI or tax ID right under its line, and searches with it', async () => {
    const strip = mountStrip({ lens: 'providers' })
    expect(strip.get('[data-testid="role-line"]').text()).toBe('Add your NPI or tax ID to see where you stand.')

    await strip.get('[data-testid="your-id-input"] input').setValue('11-3466729')
    await strip.get('[data-testid="your-id"]').trigger('submit')
    expect(strip.emitted('update')).toEqual([[{ ein: '113466729', npi: null, lens: 'providers' }]])
  })

  it('says what to type when it is neither, and searches nothing', async () => {
    const strip = mountStrip({ lens: 'providers' })
    await strip.get('[data-testid="your-id-input"] input').setValue('12345')
    await strip.get('[data-testid="your-id"]').trigger('submit')
    expect(strip.emitted('update')).toBeUndefined()
    expect(strip.text()).toContain('Enter a 10-digit NPI or a 9-digit tax ID.')
  })

  it('shows where the searched provider stands, with Change to clear it', async () => {
    const strip = mountStrip({ lens: 'providers', npi: '1043383789' }, '1043383789')
    expect(strip.find('[data-testid="your-id"]').exists()).toBe(false)
    expect(strip.get('[data-testid="role-line"]').text()).toBe('Your price is marked above.')

    await strip.get('[data-testid="change-id"]').trigger('click')
    expect(strip.emitted('update')).toEqual([[{ npi: null, ein: null }]])
  })

  it('has no box for the other roles', () => {
    expect(mountStrip({ lens: 'plans' }).find('[data-testid="your-id"]').exists()).toBe(false)
    expect(mountStrip({}).find('[data-testid="your-id"]').exists()).toBe(false)
  })
})
