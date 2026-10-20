import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { ScorecardGroup, StatisticsBasis } from '../../api/types'
import ScorecardExtremes from '../ScorecardExtremes.vue'
import StatisticsNote from '../StatisticsNote.vue'

function plugins() {
  const empty = { template: '<div />' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: empty },
      { path: '/providers/:key', name: 'scorecard', component: empty },
    ],
  })
  return [createVuetify({ theme: { defaultTheme: 'light' } }), router]
}

function basis(overrides: Partial<StatisticsBasis> = {}): StatisticsBasis {
  return { snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true }, builtAt: '2026-10-20T08:42:16Z', isCurrent: true, ...overrides }
}

function scorecardGroup(overrides: Partial<ScorecardGroup> = {}): ScorecardGroup {
  return {
    groupKey: 'essential-plan|RC|0460|facility|hospital-outpatient||negotiated',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'RC',
    code: '0460',
    codeName: null,
    categoryKey: 'facility',
    categoryLabel: 'Facility rate (revenue code only)',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'hospital-outpatient',
    settingLabel: 'Hospital outpatient department',
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per facility charge (unit not stated in the file)',
    perUnit: false,
    providerCount: 15,
    statsLevel: 'full',
    median: 26.77,
    value: 393.99,
    rateLow: 393.99,
    rateHigh: 393.99,
    percentile: 86,
    vsMedian: 14.72,
    band: 'high',
    firstRateId: 7,
    search: { code: '0460', codeType: 'RC', group: 'essential-plan|RC|0460|facility|hospital-outpatient||negotiated', ein: '131740126' },
    ...overrides,
  }
}

describe('StatisticsNote', () => {
  it('names the snapshot and stays quiet while the statistics are current', () => {
    const wrapper = mount(StatisticsNote, { props: { basis: basis() }, global: { plugins: plugins() } })
    expect(wrapper.text()).toContain('Snapshot Aug 31, 2026 · statistics saved Oct 20, 2026')
    expect(wrapper.find('[data-testid="statistics-stale"]').exists()).toBe(false)
  })

  it('warns while they are being built or rebuilt', () => {
    const building = mount(StatisticsNote, { props: { basis: basis({ isCurrent: false, builtAt: null }) }, global: { plugins: plugins() } })
    const rebuilding = mount(StatisticsNote, { props: { basis: basis({ isCurrent: false }) }, global: { plugins: plugins() } })
    expect(building.get('[data-testid="statistics-stale"]').text()).toContain('still being built')
    expect(rebuilding.get('[data-testid="statistics-stale"]').text()).toContain('being rebuilt')
  })
})

describe('ScorecardExtremes', () => {
  it('lists each group with its rate, × median and percentile, linked to the search that shows it', () => {
    const wrapper = mount(ScorecardExtremes, {
      props: { title: 'Furthest above the median', groups: [scorecardGroup()], empty: 'None above.' },
      global: { plugins: plugins() },
    })
    const item = wrapper.get('[data-testid="extreme-group"]')
    expect(item.text()).toContain('RC 0460')
    expect(item.text()).toContain('Essential Plan · Facility fee · Hospital outpatient department')
    expect(item.text()).toContain('$393.99, 14.72× the median · 86th percentile')
    const href = new URL(item.attributes('href')!, 'http://localhost')
    expect(href.pathname).toBe('/')
    expect(Object.fromEntries(href.searchParams)).toEqual({
      code: '0460',
      codeType: 'RC',
      group: 'essential-plan|RC|0460|facility|hospital-outpatient||negotiated',
      ein: '131740126',
    })
  })

  it('says so when there is nothing to list', () => {
    const wrapper = mount(ScorecardExtremes, { props: { title: 'Furthest below the median', groups: [], empty: 'None below.' }, global: { plugins: plugins() } })
    expect(wrapper.text()).toContain('None below.')
    expect(wrapper.find('[data-testid="extreme-group"]').exists()).toBe(false)
  })
})
