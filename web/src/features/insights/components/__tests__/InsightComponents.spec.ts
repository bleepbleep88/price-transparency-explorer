import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { ScorecardGroup, StatisticsBasis } from '../../api/types'
import AnswerTile from '../AnswerTile.vue'
import FacilityChargesSwitch from '../FacilityChargesSwitch.vue'
import IncludeSwitch from '../IncludeSwitch.vue'
import ScorecardExtremes from '../ScorecardExtremes.vue'
import StatisticsNote from '../StatisticsNote.vue'

// The note asks the live data status whether a rebuild is running; here none is.
vi.mock('@/features/status/useDataStatus', () => ({ useDataStatus: () => ({ status: ref({ phase: 'ready' }) }) }))

function makeRouter() {
  const empty = { template: '<div />' }
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: empty },
      { path: '/providers/:key', name: 'scorecard', component: empty },
    ],
  })
}

function plugins(router = makeRouter()) {
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
    facilityCharge: true,
    search: { code: '0460', codeType: 'RC', group: 'essential-plan|RC|0460|facility|hospital-outpatient||negotiated', ein: '131740126' },
    ...overrides,
  }
}

describe('StatisticsNote', () => {
  it('stays quiet while the figures are current', () => {
    const wrapper = mount(StatisticsNote, { props: { basis: basis() }, global: { plugins: plugins() } })
    expect(wrapper.find('[data-testid="statistics-stale"]').exists()).toBe(false)
    expect(wrapper.text()).toBe('')
  })

  it('warns in a short line while they are missing or out of date', () => {
    const missing = mount(StatisticsNote, { props: { basis: basis({ isCurrent: false, builtAt: null }) }, global: { plugins: plugins() } })
    const stale = mount(StatisticsNote, { props: { basis: basis({ isCurrent: false }) }, global: { plugins: plugins() } })
    expect(missing.get('[data-testid="statistics-stale"]').text()).toBe('No figures yet. They come with the next load.')
    expect(stale.get('[data-testid="statistics-stale"]').text()).toBe('Figures are out of date until the next load.')
  })
})

describe('ScorecardExtremes', () => {
  it('lists each service by its plain name, the bill and how far off, and opens the search that shows it', async () => {
    const router = makeRouter()
    const wrapper = mount(ScorecardExtremes, {
      props: { title: 'Most above typical', groups: [scorecardGroup()], tone: 'high', empty: 'None well above typical.' },
      global: { plugins: plugins(router) },
    })
    const row = wrapper.get('[data-testid="place-row"]')
    expect(row.text()).toContain('Hospital · Essential Plan · about 15 times typical')
    expect(row.text()).toContain('$394')
    expect(row.text()).not.toMatch(/median|percentile|×|RC|0460|modifier|EIN/)
    await row.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({
      code: '0460',
      codeType: 'RC',
      group: 'essential-plan|RC|0460|facility|hospital-outpatient||negotiated',
      ein: '131740126',
    })
  })

  it('says so when there is nothing to list', () => {
    const wrapper = mount(ScorecardExtremes, {
      props: { title: 'Most below typical', groups: [], tone: 'low', empty: 'None well below typical.' },
      global: { plugins: plugins() },
    })
    expect(wrapper.text()).toContain('None well below typical.')
    expect(wrapper.find('[data-testid="place-row"]').exists()).toBe(false)
  })
})

describe('FacilityChargesSwitch', () => {
  it('has a short label with what it adds, and cannot be turned on with nothing to add', () => {
    const some = mount(FacilityChargesSwitch, { props: { modelValue: false, count: 128 }, global: { plugins: plugins() } })
    const none = mount(FacilityChargesSwitch, { props: { modelValue: false, count: 0 }, global: { plugins: plugins() } })
    expect(some.text()).toBe('Include facility-only charges (128)')
    expect(some.find('input').attributes('disabled')).toBeUndefined()
    expect(none.find('input').attributes('disabled')).toBeDefined()
  })
})

describe('IncludeSwitch', () => {
  it('names what it adds with the count, and cannot be turned on with nothing to add', () => {
    const some = mount(IncludeSwitch, { props: { modelValue: false, count: 312, label: 'Include health centers', testid: 'health-centers' }, global: { plugins: plugins() } })
    const none = mount(IncludeSwitch, { props: { modelValue: false, count: 0, label: 'Include health centers', testid: 'health-centers' }, global: { plugins: plugins() } })
    expect(some.text()).toBe('Include health centers (312)')
    expect(some.find('[data-testid="health-centers"]').exists()).toBe(true)
    expect(none.text()).toBe('Include health centers')
    expect(none.find('input').attributes('disabled')).toBeDefined()
  })
})

describe('AnswerTile', () => {
  it('shows a label, one big figure and one line', () => {
    const wrapper = mount(AnswerTile, { props: { label: 'Same price in both plans', figure: '77%', line: 'of 8,544 prices' }, global: { plugins: plugins() } })
    expect(wrapper.get('[data-testid="answer-figure"]').text()).toBe('77%')
    expect(wrapper.get('[data-testid="answer-line"]').text()).toBe('of 8,544 prices')
  })
})
