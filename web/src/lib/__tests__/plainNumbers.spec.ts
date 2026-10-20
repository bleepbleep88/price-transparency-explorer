import { describe, expect, it } from 'vitest'
import {
  aboutDollars,
  aboutTimes,
  bandFor,
  differenceLine,
  dollars,
  feeLabel,
  fewProvidersLine,
  gapLine,
  lessPerCase,
  perCaseLine,
  positionLabel,
  positionLine,
  positionParts,
  rangeLine,
  scorecardLine,
  spreadLine,
  typicalLine,
  typicalPrice,
  yearlyLine,
  type PriceSummary,
} from '../plainNumbers'

// The 45378 Ambetter surgery-center group (31 providers), a 5-to-9 group and a 3-provider group, as the API sends them.
const colonoscopy: PriceSummary = { providers: 31, min: 226.51, max: 1154.16, p10: 371.78, p25: 454.44, median: 578.31, p75: 627.5, p90: 663.64 }
const small: PriceSummary = { providers: 7, min: 185.4, max: 205.41, p10: null, p25: 190, median: 205.41, p75: 205.41, p90: null }
const tiny: PriceSummary = { providers: 3, min: 194.23, max: 233.43, p10: null, p25: null, median: null, p75: null, p90: null }

const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|index/i

describe('dollars', () => {
  it('drops cents from $100 up, keeps them below, and drops the sign', () => {
    expect(dollars(578.31)).toBe('$578')
    expect(dollars(1154.16)).toBe('$1,154')
    expect(dollars(99.99)).toBe('$99.99')
    expect(dollars(12.3)).toBe('$12.30')
    expect(dollars(48)).toBe('$48')
    expect(dollars(0.17)).toBe('$0.17')
    expect(dollars(-123.86)).toBe('$124')
  })

  it('rounds estimates to three figures', () => {
    expect(aboutDollars(148632)).toBe('$149,000')
    expect(aboutDollars(12345)).toBe('$12,300')
    expect(aboutDollars(1004)).toBe('$1,000')
    expect(aboutDollars(987.4)).toBe('$987')
    expect(aboutDollars(42.5)).toBe('$42.50')
  })

  it('says times with one decimal under 10 and whole numbers from 10', () => {
    expect(aboutTimes(2.43)).toBe('about 2.4 times')
    expect(aboutTimes(6.02)).toBe('about 6 times')
    expect(aboutTimes(9.96)).toBe('about 10 times')
    expect(aboutTimes(20.4)).toBe('about 20 times')
  })
})

describe('typical price and range', () => {
  it('calls the middle price typical from 5 providers, never under', () => {
    expect(typicalPrice(colonoscopy)).toBe(578.31)
    expect(typicalLine(colonoscopy)).toBe('Typical price: $578')
    expect(typicalLine(small, 'per unit')).toBe('Typical price: $205 per unit')
    expect(typicalPrice(tiny)).toBeNull()
    expect(typicalLine(tiny)).toBeNull()
    // Even if a median slipped through for a tiny group, it stays unsaid.
    expect(typicalLine({ ...tiny, median: 200 })).toBeNull()
  })

  it('says where most providers are from 10 providers, the full run from 5, and nothing under 5', () => {
    expect(rangeLine(colonoscopy)).toBe('Most providers get between $372 and $664.')
    expect(rangeLine(small)).toBe('Prices run from $185 to $205.')
    expect(rangeLine(tiny)).toBeNull()
  })

  it('says one price once when the ends round to the same amount', () => {
    expect(rangeLine({ ...colonoscopy, p10: 167.09, p90: 167.09 })).toBe('Most providers get $167.')
    expect(rangeLine({ ...small, min: 300, max: 300 })).toBe('Every provider gets $300.')
  })

  it('names how few providers there are when there is nothing to compare', () => {
    expect(fewProvidersLine(3)).toBe('Only 3 providers: too few to compare.')
    expect(fewProvidersLine(1)).toBe('Only 1 provider: too few to compare.')
  })
})

describe('a provider against the others', () => {
  it('words the three bands', () => {
    expect(positionLabel('low')).toBe('Lower than most')
    expect(positionLabel('typical')).toBe('About typical')
    expect(positionLabel('high')).toBe('Higher than most')
    expect(positionLabel(null)).toBeNull()
    expect(positionLabel('other')).toBeNull()
  })

  it('bands a price the way the API does', () => {
    expect(bandFor(454.44, colonoscopy)).toBe('low')
    expect(bandFor(454.45, colonoscopy)).toBe('typical')
    expect(bandFor(627.5, colonoscopy)).toBe('high')
    expect(bandFor(578.31, colonoscopy)).toBe('typical')
    // At the lower quarter but also at typical (prices bunched up): not lower than most.
    expect(bandFor(205.41, { ...small, p25: 205.41 })).toBe('typical')
    expect(bandFor(200, tiny)).toBeNull()
  })

  it('says the difference in dollars, "about the same" within 5%, and times from twice up', () => {
    expect(differenceLine(566.31, 578.31)).toBe('about the same as typical')
    expect(differenceLine(550, 578.31)).toBe('about the same as typical')
    expect(differenceLine(549, 578.31)).toBe('$29.31 less than typical')
    expect(differenceLine(143.75, 167.09)).toBe('$23.34 less than typical')
    expect(differenceLine(707.31, 578.31)).toBe('$129 more than typical')
    expect(differenceLine(1154.16, 578.31)).toBe('$576 more than typical')
    expect(differenceLine(1405.33, 578.31)).toBe('about 2.4 times the typical price')
    expect(differenceLine(100, null)).toBeNull()
    expect(differenceLine(100, 0)).toBeNull()
  })

  it('puts position and dollars together, without saying "typical" twice', () => {
    expect(positionLine('low', 143.75, 167.09)).toBe('Lower than most · $23.34 less than typical')
    expect(positionLine('typical', 578.31, 578.31)).toBe('About typical')
    expect(positionLine('typical', 610, 578.31)).toBe('About typical · $31.69 more than typical')
    expect(positionLine('high', 1405.33, 578.31, ', ')).toBe('Higher than most, about 2.4 times the typical price')
    // Bunched-up prices: lower than most, yet only a little.
    expect(positionLine('low', 570, 578.31)).toBe('Lower than most · about the same as typical')
    expect(positionLine(null, 300, null)).toBeNull()
  })

  it('keeps position and dollars apart for stacked layouts, dropping dollars that only repeat "about typical"', () => {
    expect(positionParts('low', 143.75, 167.09)).toEqual({ label: 'Lower than most', difference: '$23.34 less than typical' })
    expect(positionParts('typical', 578.31, 578.31)).toEqual({ label: 'About typical', difference: null })
    expect(positionParts('low', 570, 578.31)).toEqual({ label: 'Lower than most', difference: 'about the same as typical' })
    expect(positionParts(null, 300, null)).toEqual({ label: null, difference: null })
  })
})

describe('the lower-priced quarter', () => {
  it('says what the lower-priced quarter get and how much less than typical, from 10 providers', () => {
    expect(gapLine(colonoscopy)).toBe('The lower-priced quarter of providers get $454 or less: $124 less than typical.')
    // No gap: the line would only repeat the typical price.
    expect(gapLine({ ...colonoscopy, p25: 578.31 })).toBeNull()
    expect(gapLine(small)).toBeNull()
    expect(gapLine(tiny)).toBeNull()
  })

  it('says it per case for plans and employers, per unit for per-unit prices', () => {
    expect(perCaseLine(123.86)).toBe('$124 less per case at a lower-priced provider.')
    expect(perCaseLine(0.05, true)).toBe('$0.05 less per unit at a lower-priced provider.')
    expect(perCaseLine(0)).toBe('No less per case at a lower-priced provider.')
    expect(perCaseLine(null)).toBeNull()
    expect(lessPerCase(123.86)).toBe('$124 less per case')
    expect(lessPerCase(0)).toBe('No less')
  })

  it('gives a yearly figure only from the reader\'s own case count', () => {
    expect(yearlyLine(123.86, 1200)).toBe('About $149,000 less a year for your 1,200 cases at lower-priced providers.')
    expect(yearlyLine(123.86, 1)).toBe('About $124 less a year for your 1 case at lower-priced providers.')
    expect(yearlyLine(123.86, null)).toBeNull()
    expect(yearlyLine(0, 1200)).toBeNull()
    expect(yearlyLine(null, 1200)).toBeNull()
  })
})

describe('how much prices vary', () => {
  it('compares the high end with the low end, leaving out the extremes, from 10 providers', () => {
    expect(spreadLine(colonoscopy)).toBe('Even leaving out the extremes, the high end is about 1.8 times the low end.')
    expect(spreadLine({ ...colonoscopy, p10: 100, p90: 602 })).toBe('Even leaving out the extremes, the high end is about 6 times the low end.')
    // Within 5%: "Most providers get between $167 and $170." already says it.
    expect(spreadLine({ ...colonoscopy, p10: 167.09, p90: 170 })).toBeNull()
    expect(spreadLine(small)).toBeNull()
    expect(spreadLine({ ...colonoscopy, p10: 0 })).toBeNull()
  })
})

describe('scorecard', () => {
  it('sums a provider up across its services in percent, "about typical" within 5%, times from twice up', () => {
    expect(scorecardLine(1.08, 26)).toBe('Usually about 8% more than typical (across 26 services)')
    expect(scorecardLine(0.88, 26)).toBe('Usually about 12% less than typical (across 26 services)')
    expect(scorecardLine(1.04, 26)).toBe('About typical (across 26 services)')
    expect(scorecardLine(0.94, 5)).toBe('Usually about 6% less than typical (across 5 services)')
    expect(scorecardLine(2.4, 7)).toBe('Usually about 2.4 times the typical price (across 7 services)')
  })

  it('says nothing from fewer than 5 services', () => {
    expect(scorecardLine(1.3, 4)).toBeNull()
    expect(scorecardLine(null, 26)).toBeNull()
  })
})

describe('words', () => {
  it('calls the two bills a doctor\'s fee and a facility fee', () => {
    expect(feeLabel('professional')).toBe("Doctor's fee")
    expect(feeLabel('facility')).toBe('Facility fee')
    expect(feeLabel('other')).toBe('Fee')
  })

  it('never uses a statistics word', () => {
    const lines = [
      typicalLine(colonoscopy),
      rangeLine(colonoscopy),
      rangeLine(small),
      gapLine(colonoscopy),
      spreadLine(colonoscopy),
      perCaseLine(123.86),
      yearlyLine(123.86, 1200),
      positionLine('high', 1405.33, 578.31),
      positionLine('low', 143.75, 167.09),
      scorecardLine(1.08, 26),
      fewProvidersLine(3),
    ]
    for (const line of lines) expect(line).not.toMatch(BANNED)
  })
})
