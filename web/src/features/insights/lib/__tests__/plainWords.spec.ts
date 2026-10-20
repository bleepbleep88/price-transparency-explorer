import { describe, expect, it } from 'vitest'
import { groupLine, pairWords, paysWords, position, rangeWords, unitSuffix } from '../plainWords'

// Decision 89 on the insights pages: no statistics jargon, "about the same" within 5%, "times" only from twice, and the
// position and the dollars on one row never contradict each other.

describe('position', () => {
  it('says "About typical" only within 5% of typical, and then alone', () => {
    expect(position('typical', 103, 100)).toEqual({ label: 'About typical', difference: null })
  })

  it('gives a middle-band price far from typical its dollars and no "About typical"', () => {
    // 84 of 124 providers get $51.45, so $109.76 is in the middle band, yet about twice the typical price.
    expect(position('typical', 109.76, 51.45)).toEqual({ label: null, difference: 'about 2.1 times the typical price' })
    expect(position('typical', 61.22, 40)).toEqual({ label: null, difference: '$21.22 more than typical (about 53% more)' })
  })

  it('never puts "about the same" next to "Lower than most" or "Higher than most"', () => {
    expect(position('high', 88.12, 87.75)).toEqual({ label: 'Higher than most', difference: '$0.37 more than typical' })
    expect(position('low', 73.6, 74.55)).toEqual({ label: 'Lower than most', difference: '$0.95 less than typical' })
  })

  it('says the share the lists are ranked by next to the dollars', () => {
    // $31.81 against a typical price shown as $252: $220.19 less, to the cent, as the shared helper writes differences.
    expect(position('low', 31.81, 251.81)).toEqual({ label: 'Lower than most', difference: '$220.19 less than typical (about 87% less)' })
  })

  it('takes the dollars between the rate as shown and the typical price as shown', () => {
    // $1,404.32 against a typical $1,325 (1,324.60 unrounded) reads $79.32 more, not $79.72.
    expect(position('high', 1404.32, 1324.6).difference).toBe('$79.32 more than typical (about 6% more)')
  })

  it('leaves the label alone when rounding the typical price leaves the difference the wrong way', () => {
    // $100.40 against a typical $100.45, which shows as $100.
    expect(position('low', 100.4, 100.45)).toEqual({ label: 'Lower than most', difference: null })
  })

  it('puts the unit after the dollars, and says nothing without a typical price', () => {
    expect(position('low', 0.11, 0.15, ' per unit').difference).toBe('$0.04 per unit less than typical (about 27% less)')
    expect(position(null, 170, null)).toEqual({ label: null, difference: null })
  })
})

describe('what one plan pays against the other', () => {
  it('says "the same" for a pair only when the two rates are equal', () => {
    expect(pairWords({ same: true, productValue: 603.82, versusValue: 603.82, ratio: 1 })).toBe('the same')
    expect(pairWords({ same: false, productValue: 2665.57, versusValue: 2677.58, ratio: 1 })).toBe('almost the same ($12.01 less)')
    expect(pairWords({ same: false, productValue: 395.22, versusValue: 393.81, ratio: 1 })).toBe('almost the same ($1.41 more)')
    expect(pairWords({ same: false, productValue: 90, versusValue: 100, ratio: 0.9 })).toBe('about 10% less')
    expect(pairWords({ same: false, productValue: 2665.574, versusValue: 2677.576, ratio: 1 })).toBe('almost the same ($12.01 less)')
    expect(pairWords({ same: false, productValue: 603.821, versusValue: 603.824, ratio: 1 })).toBe('almost the same')
  })

  it('reads a rounded summary point as "about the same" and a big one in times', () => {
    expect(paysWords(1)).toBe('about the same')
    expect(paysWords(0.8)).toBe('about 20% less')
    expect(paysWords(25.13)).toBe('about 25 times as much')
    expect(paysWords(1.9)).toBe('about 90% more')
    expect(paysWords(null)).toBeNull()
  })
})

describe('rangeWords', () => {
  it('says "times" only from twice, dollars under that, and "about the same" within 5%', () => {
    expect(rangeWords(2978.01, 11238.91)).toBe('the high end is about 3.8 times the low end')
    // Shown as $1,277 to $1,422, so $145 more, though the unrounded gap is $144.25.
    expect(rangeWords(1277.48, 1421.73)).toBe('the high end is $145 more than the low end')
    expect(rangeWords(24.76, 25.11)).toBe('most providers get about the same price')
    expect(rangeWords(167.09, 167.09)).toBe('most providers get the same price')
    expect(rangeWords(100, 199)).not.toContain('times')
  })

  it('puts the unit after the dollars', () => {
    expect(rangeWords(0.1, 0.18, ' per unit')).toBe('the high end is $0.08 per unit more than the low end')
  })
})

describe('labels', () => {
  it("names the two bills as people do and the unit of a per-unit price", () => {
    expect(groupLine({ productLabel: 'Essential Plan', feeType: 'professional', settingLabel: "Doctor's office", modifiers: [] })).toBe(
      "Essential Plan · Doctor's fee · Doctor's office",
    )
    expect(unitSuffix({ perUnit: true })).toBe(' per unit')
    expect(unitSuffix({ perUnit: false })).toBe('')
  })
})
