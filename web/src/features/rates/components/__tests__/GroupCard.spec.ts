import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { money } from '@/lib/format'
import type { RateGroup } from '../../api/types'
import GroupCard from '../GroupCard.vue'

// Shaped like a real 45378 group from the API (Ambetter, surgery-center facility fees).
function makeGroup(overrides: Partial<RateGroup> = {}): RateGroup {
  return {
    key: 'ambetter-from-fidelis-care|CPT|45378|facility|asc||negotiated',
    productKey: 'ambetter-from-fidelis-care',
    productLabel: 'Ambetter from Fidelis Care',
    codeType: 'CPT',
    code: '45378',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'asc',
    settingLabel: 'Ambulatory surgery center',
    settingNote: null,
    places: [{ pos: '24', name: 'Ambulatory surgical center' }],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per procedure',
    perUnit: false,
    billedAs: [{ billingCodeType: 'RC', billingCode: '0490', label: 'Revenue code 0490 (Ambulatory surgery)' }],
    providerCount: 31,
    rateCount: 31,
    statsLevel: 'full',
    stats: {
      min: 226.51,
      p10: 371.78,
      p25: 454.44,
      median: 578.31,
      p75: 627.5,
      p90: 663.64,
      max: 1154.16,
      spreadMultiple: 5.1,
      p90ToP10: 1.79,
    },
    lowerQuartileGap: 123.87,
    listedRates: [],
    highlights: [],
    notes: ['The plan files these facility rates under revenue code 0490 (Ambulatory surgery).'],
    ...overrides,
  }
}

function mountCard(group: RateGroup, selected = false) {
  const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
  return mount(GroupCard, { props: { group, selected }, global: { plugins: [vuetify] } })
}

describe('GroupCard', () => {
  it('lists the rates without statistics when fewer than 5 providers', () => {
    const wrapper = mountCard(
      makeGroup({
        statsLevel: 'list',
        providerCount: 3,
        rateCount: 3,
        listedRates: [180.5, 212, 260.75],
        stats: { min: 180.5, p10: null, p25: null, median: null, p75: null, p90: null, max: 260.75, spreadMultiple: null, p90ToP10: null },
        lowerQuartileGap: null,
      }),
    )

    const list = wrapper.get('[data-testid="stats-list"]')
    for (const rate of [180.5, 212, 260.75]) expect(list.text()).toContain(money(rate))
    expect(wrapper.find('[data-testid="stats-basic"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="stats-full"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Median')
  })

  it('shows median and range for 5 to 9 providers, without the quartile gap', () => {
    const wrapper = mountCard(
      makeGroup({
        statsLevel: 'basic',
        providerCount: 6,
        rateCount: 6,
        stats: { min: 150, p10: null, p25: null, median: 205.4, p75: null, p90: null, max: 320, spreadMultiple: 2.13, p90ToP10: null },
        lowerQuartileGap: null,
      }),
    )

    const basic = wrapper.get('[data-testid="stats-basic"]')
    expect(basic.text()).toContain(money(205.4))
    expect(basic.text()).toContain(`Range ${money(150)} to ${money(320)}`)
    expect(wrapper.find('[data-testid="stats-full"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="quartile-gap"]').exists()).toBe(false)
  })

  it('shows full statistics and the gap to the lower quartile for 10+ providers', () => {
    const wrapper = mountCard(makeGroup())

    const full = wrapper.get('[data-testid="stats-full"]')
    for (const value of [454.44, 578.31, 627.5]) expect(full.text()).toContain(money(value))
    const gap = wrapper.get('[data-testid="quartile-gap"]').text()
    expect(gap).toContain(`${money(123.87)} gap to the lower quartile`)
    expect(gap).toContain(`the median rate is ${money(578.31)}`)
    expect(gap).toContain(`a quarter of providers are paid ${money(454.44)} or less`)
  })

  it('says there is no gap when the 25th percentile equals the median', () => {
    const wrapper = mountCard(makeGroup({ lowerQuartileGap: 0 }))

    expect(wrapper.get('[data-testid="no-gap"]').text()).toBe('No gap: the 25th percentile and the median are the same rate.')
    expect(wrapper.find('[data-testid="quartile-gap"]').exists()).toBe(false)
  })

  it('describes the gap in neutral words', () => {
    expect(mountCard(makeGroup()).text().toLowerCase()).not.toMatch(/sav(e|ed|ing|ings)/)
  })

  it("shows where the searched provider sits and emits the group key", async () => {
    const group = makeGroup({
      highlights: [
        { providerKey: '113466729', displayName: 'Example Surgery Center', rate: 627.5, percentile: 75, vsMedian: 1.09, aboveP25: 173.06 },
      ],
    })
    const wrapper = mountCard(group)

    const highlights = wrapper.get('[data-testid="group-highlights"]')
    expect(highlights.text()).toContain('Example Surgery Center')
    expect(highlights.text()).toContain('75th percentile')
    expect(highlights.text()).toContain(`${money(173.06)} above the 25th percentile`)

    await wrapper.findAll('button').find((button) => button.text() === 'Show these providers')!.trigger('click')
    expect(wrapper.emitted('showProviders')).toEqual([[group.key]])
  })
})
