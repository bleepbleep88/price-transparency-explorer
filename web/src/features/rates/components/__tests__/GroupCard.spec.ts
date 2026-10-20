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
    matchedProviders: 0,
    highlights: [],
    notes: ['The plan files these facility rates under revenue code 0490 (Ambulatory surgery).'],
    ...overrides,
  }
}

function mountCard(group: RateGroup, selected = false, highlightHeading?: string) {
  const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
  return mount(GroupCard, { props: { group, selected, highlightHeading }, global: { plugins: [vuetify] } })
}

const BANNED = /percentile|median|quartile|×|spread|index/i

describe('GroupCard', () => {
  it('lists the prices with nothing to compare when fewer than 5 providers', () => {
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
    expect(list.text()).toContain('Only 3 providers: too few to compare. Their prices:')
    for (const rate of [180.5, 212, 260.75]) expect(list.text()).toContain(money(rate))
    expect(wrapper.find('[data-testid="typical-price"]').exists()).toBe(false)
    expect(wrapper.text()).not.toMatch(/typical/i)
  })

  it('says "its price" for a single provider', () => {
    const wrapper = mountCard(
      makeGroup({
        statsLevel: 'list',
        providerCount: 1,
        rateCount: 1,
        listedRates: [1209.45],
        stats: { min: 1209.45, p10: null, p25: null, median: null, p75: null, p90: null, max: 1209.45, spreadMultiple: null, p90ToP10: null },
        lowerQuartileGap: null,
      }),
    )

    expect(wrapper.get('[data-testid="stats-list"]').text()).toContain('Only 1 provider: too few to compare. Its price:')
  })

  it('gives the typical price and the full run of prices for 5 to 9 providers, nothing more', () => {
    const wrapper = mountCard(
      makeGroup({
        statsLevel: 'basic',
        providerCount: 6,
        rateCount: 6,
        stats: { min: 150, p10: null, p25: 180, median: 205.4, p75: 260, p90: null, max: 320, spreadMultiple: null, p90ToP10: null },
        lowerQuartileGap: null,
      }),
    )

    const basic = wrapper.get('[data-testid="stats-basic"]')
    expect(basic.get('[data-testid="typical-price"]').text()).toBe('$205')
    expect(basic.get('[data-testid="range-line"]').text()).toBe('Prices run from $150 to $320.')
    expect(wrapper.find('[data-testid="quartile-gap"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="spread-line"]').exists()).toBe(false)
    // No "most providers" band on the bar: that needs 10 providers.
    expect(wrapper.find('.range-bar__band').exists()).toBe(false)
  })

  it('adds where most providers are and the lower-priced quarter from 10 providers, saying the range once', () => {
    const wrapper = mountCard(makeGroup())

    const full = wrapper.get('[data-testid="stats-full"]')
    expect(full.get('[data-testid="typical-price"]').text()).toBe('$578')
    expect(full.get('[data-testid="range-line"]').text()).toBe('Most providers get between $372 and $664.')
    // "Most providers get between $372 and $664" already says how much prices vary; no "about 1.8 times" after it.
    expect(full.find('[data-testid="spread-line"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="quartile-gap"]').text()).toBe(
      'The lower-priced quarter of providers get $454 or less: $124 less than typical.',
    )
    expect(wrapper.get('[role="img"]').attributes('aria-label')).toBe(
      'Lowest $227, highest $1,154. Most providers get between $372 and $664. Typical price: $578.',
    )
  })

  it('says one price once when most providers get it', () => {
    const flat = { min: 143.75, p10: 167.09, p25: 167.09, median: 167.09, p75: 167.09, p90: 167.09, max: 185.4, spreadMultiple: 1.29, p90ToP10: 1 }
    const wrapper = mountCard(makeGroup({ providerCount: 51, stats: flat, lowerQuartileGap: 0 }))

    expect(wrapper.get('[data-testid="range-line"]').text()).toBe('Most providers get $167.')
    expect(wrapper.find('[data-testid="spread-line"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="quartile-gap"]').exists()).toBe(false)
  })

  it("calls the fee a doctor's fee or a facility fee, and names what it's filed under", () => {
    const facility = mountCard(makeGroup())
    expect(facility.get('.v-card-title').text()).toBe('Facility fee · Ambulatory surgery center')
    expect(facility.get('[data-testid="group-billing"]').text()).toBe('Filed under revenue code 0490 (Ambulatory surgery)')

    const doctor = mountCard(
      makeGroup({ feeType: 'professional', billedAs: [], modifiers: [{ code: '33', label: 'Preventive service' }] }),
    )
    expect(doctor.get('.v-card-title').text()).toBe("Doctor's fee · Ambulatory surgery center")
    expect(doctor.get('[data-testid="group-billing"]').text()).toBe('Preventive service (modifier 33)')
  })

  it('uses no statistics words and no savings claims', () => {
    const highlights = [
      { providerKey: 'a', displayName: 'A', rate: 627.5, percentile: 75, vsMedian: 1.09, aboveP25: 173.06, rateLow: 627.5, rateHigh: 627.5 },
    ]
    for (const group of [makeGroup({ highlights }), makeGroup({ statsLevel: 'basic', providerCount: 6, lowerQuartileGap: null })]) {
      const text = mountCard(group).text()
      expect(text).not.toMatch(BANNED)
      expect(text.toLowerCase()).not.toMatch(/sav(e|ed|ing|ings)/)
    }
  })

  it('says how the searched provider compares, in words and dollars, and emits the group key', async () => {
    const group = makeGroup({
      highlights: [
        { providerKey: '113466729', displayName: 'Example Surgery Center', rate: 627.5, percentile: 75, vsMedian: 1.09, aboveP25: 173.06, rateLow: 627.5, rateHigh: 627.5 },
        { providerKey: '2', displayName: 'Example Endoscopy', rate: 578.31, percentile: 50, vsMedian: 1, aboveP25: 123.87, rateLow: 578.31, rateHigh: 578.31 },
      ],
    })
    const wrapper = mountCard(group)

    const highlights = wrapper.get('[data-testid="group-highlights"]')
    expect(highlights.text()).toContain('How your provider compares')
    const lines = highlights.findAll('[data-testid="highlight-line"]')
    expect(lines.map((line) => [line.get('strong').text(), line.get('[data-testid="highlight-position"]').text()])).toEqual([
      // $627.50 against the "$578" the card shows: $49.50 more.
      ['Example Surgery Center', 'Higher than most · $49.50 more than typical'],
      ['Example Endoscopy', 'About typical'],
    ])
    // The provider's own price is the copper chip, to the cent.
    expect(highlights.get('[data-testid="your-rate"]').text()).toBe(money(627.5))
    // Screen readers hear whose price the copper dot marks.
    expect(wrapper.get('[role="img"]').attributes('aria-label')).toContain('Marked: Example Surgery Center, $627.50.')

    await wrapper.findAll('button').find((button) => button.text() === 'Show these providers')!.trigger('click')
    expect(wrapper.emitted('showProviders')).toEqual([[group.key]])
  })

  it('names the filter instead of "your provider" for a name search', () => {
    const group = makeGroup({
      highlights: [{ providerKey: 'a', displayName: 'Hudson Valley Gastroenterology PC', rate: 627.5, percentile: 75, vsMedian: 1.09, aboveP25: 173.06, rateLow: 627.5, rateHigh: 627.5 }],
    })
    const heading = 'How providers whose name contains "gastro" compare'
    const highlights = mountCard(group, false, heading).get('[data-testid="group-highlights"]')
    expect(highlights.text()).toContain(heading)
    expect(highlights.text()).not.toContain('your provider')
  })
})
