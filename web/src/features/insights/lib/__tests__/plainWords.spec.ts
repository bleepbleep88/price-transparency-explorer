import { describe, expect, it } from 'vitest'
import { aboutTimes, dollars, feeWords, groupLine, overallText, paysWords, spreadWords, versusTypical } from '../plainWords'

// Decision 89: no statistics jargon on screen. These are the words every insights number goes through.

describe('versusTypical', () => {
  it('says a rate in dollars against the typical price', () => {
    expect(versusTypical(61.61, 71.64)).toBe('$10.03 less than typical')
    expect(versusTypical(2900, 2620)).toBe('$280 more than typical')
  })

  it('calls anything within 5% about the same, and twice or more a multiple', () => {
    expect(versusTypical(103, 100)).toBe('about the same as typical')
    expect(versusTypical(240, 100)).toBe('about 2.4 times the typical price')
    expect(versusTypical(2117.61, 472.51)).toBe('about 4.5 times the typical price')
  })

  it('says nothing without a typical price', () => {
    expect(versusTypical(170, null)).toBeNull()
  })
})

describe('numbers in words', () => {
  it('rounds multiples to one decimal under 10 and to whole numbers from 10', () => {
    expect(aboutTimes(3.77)).toBe('about 3.8 times')
    expect(aboutTimes(20.15)).toBe('about 20 times')
    expect(aboutTimes(2)).toBe('about 2 times')
  })

  it('keeps cents only under $100', () => {
    expect(dollars(-1.5)).toBe('$1.50')
    expect(dollars(2692.36)).toBe('$2,692')
  })

  it('reads a provider overall without an index', () => {
    expect(overallText(1.08)).toBe('Usually about 8% more than typical')
    expect(overallText(0.85)).toBe('Usually about 15% less than typical')
    expect(overallText(0.96)).toBe('About typical')
  })

  it('says what one plan pays against the other', () => {
    expect(paysWords(1)).toBe('the same')
    expect(paysWords(0.8)).toBe('about 20% less')
    expect(paysWords(1.1)).toBe('about 10% more')
    expect(paysWords(25.13)).toBe('about 25 times as much')
    expect(paysWords(1.004)).toBe('almost the same')
  })

  it('says a spread as the high end against the low end', () => {
    expect(spreadWords(2978.01, 11238.91)).toBe('the high end is about 3.8 times the low end')
    expect(spreadWords(167.09, 167.09)).toBe('most providers get the same price')
    expect(spreadWords(null, 10)).toBeNull()
  })

  it("names the two bills as people do", () => {
    expect(feeWords('professional')).toBe("Doctor's fee")
    expect(groupLine({ productLabel: 'Essential Plan', feeType: 'facility', settingLabel: 'Ambulatory surgery center', modifiers: [] })).toBe(
      'Essential Plan · Facility fee · Ambulatory surgery center',
    )
  })
})
