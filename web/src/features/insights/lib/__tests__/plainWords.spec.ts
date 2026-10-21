import { describe, expect, it } from 'vitest'
import { modifierWords } from '@/lib/plainNames'
import { dollars } from '@/lib/plainNumbers'
import { groupLine, pairWords, paysWords, planName, rangeText, rowLine, spreadDollars, tellApart, unitSuffix, versusTypical } from '../plainWords'

// Decisions 89 and 90 on the insights pages: no statistics words, "about" within 5%, "times" only from twice, and as
// few words as can say it.

describe('versusTypical', () => {
  it('says how far off in three or four words', () => {
    expect(versusTypical(118, 100)).toBe('18% above typical')
    expect(versusTypical(31.81, 251.81)).toBe('87% below typical')
    expect(versusTypical(109.76, 51.45)).toBe('About 2.1 times typical')
    expect(versusTypical(103, 100, 'typical')).toBe('About typical')
  })

  it('gives a price placed lower or higher than most its dollars, even within 5%', () => {
    expect(versusTypical(73.6, 74.55, 'low')).toBe('$0.95 below typical')
    expect(versusTypical(88.12, 87.75, 'high')).toBe('$0.37 above typical')
  })

  it('takes those dollars between the figures as shown, and says only "about" when rounding leaves none', () => {
    // $1,404.32 (shown $1,404) against a typical $1,384: within 5%, but placed higher than most.
    expect(versusTypical(1404.32, 1384, 'high')).toBe('$20 above typical')
    // $100.40 against a typical $100.45, which shows as $100: the difference would point the wrong way.
    expect(versusTypical(100.4, 100.45, 'low')).toBe('About typical')
  })

  it('says nothing without a typical price', () => {
    expect(versusTypical(170, null)).toBeNull()
  })
})

describe('what one plan pays against the other', () => {
  it('says "the same" for a pair only when the two rates are equal', () => {
    expect(pairWords({ same: true, productValue: 603.82, versusValue: 603.82, ratio: 1 })).toBe('the same')
    // $395 and $394 as shown beside it.
    expect(pairWords({ same: false, productValue: 395.22, versusValue: 393.81, ratio: 1 })).toBe('almost the same ($1 more)')
    expect(pairWords({ same: false, productValue: 90.1, versusValue: 90.5, ratio: 1 })).toBe('almost the same ($0.40 less)')
    expect(pairWords({ same: false, productValue: 603.821, versusValue: 603.824, ratio: 1 })).toBe('almost the same')
    expect(pairWords({ same: false, productValue: 90, versusValue: 100, ratio: 0.9 })).toBe('about 10% less')
  })

  it('reads a rounded summary point as "about the same" and a big one in times', () => {
    expect(paysWords(1)).toBe('about the same')
    expect(paysWords(0.8)).toBe('about 20% less')
    expect(paysWords(25.13)).toBe('about 25 times as much')
    expect(paysWords(1.9)).toBe('about 90% more')
    expect(paysWords(null)).toBeNull()
  })
})

describe('ranges', () => {
  it('writes a range with an en dash and its spread between the ends as shown', () => {
    expect(rangeText(389.4, 625.2, dollars)).toBe('$389 – $625')
    expect(rangeText(167.09, 167.4, dollars)).toBe('$167')
    // Shown as $1,277 – $1,422, so $145 apart, though the unrounded ends are $144.25 apart.
    expect(spreadDollars(1277.48, 1421.73)).toBe('$145')
    expect(spreadDollars(0.1, 0.18)).toBe('$0.08')
  })
})

describe('names on the main view', () => {
  const office = { feeType: 'professional', settingKey: 'office', settingLabel: "Doctor's office", modifiers: [], productLabel: 'Essential Plan', perUnit: false }

  it('calls a plan what people call it', () => {
    expect(planName('Ambetter from Fidelis Care')).toBe('Ambetter')
    expect(planName('Essential Plan')).toBe('Essential Plan')
  })

  it('says the bill, the word that tells it apart, the plan and "per unit"', () => {
    expect(rowLine(office)).toBe('Doctor · in the office · Essential Plan')
    expect(rowLine(office, 'left side')).toBe('Doctor · in the office · left side · Essential Plan')
    expect(rowLine({ ...office, feeType: 'facility', settingKey: 'asc', productLabel: 'Ambetter from Fidelis Care', perUnit: true })).toBe(
      'Surgery center · Ambetter · per unit',
    )
    expect(rowLine({ ...office, productLabel: undefined })).toBe('Doctor · in the office')
  })

  it('shows a modifier only when it tells two rows of the same service, bill and plan apart', () => {
    const modifier = (code: string, label: string) => ({ code, label })
    const procedure = modifier('25', 'Separate visit on the same day as a procedure (modifier 25)')
    const video = modifier('95', 'Telehealth (audio and video) (modifier 95)')
    const plain = { key: 'office', modifiers: [] as { code: string; label: string }[] }
    const withProcedure = { key: 'office', modifiers: [procedure] }
    const withBoth = { key: 'office', modifiers: [procedure, video] }
    const alone = { key: 'er', modifiers: [modifier('LT', 'Left side (modifier LT)')] }
    const unnamed = { key: 'home', modifiers: [modifier('SS', 'Modifier SS')] }
    const unnamedToo = { key: 'home', modifiers: [] as { code: string; label: string }[] }
    const apart = tellApart([plain, withProcedure, withBoth, alone, unnamed, unnamedToo], (row) => row.key)
    expect(apart.get(plain)).toBe('')
    expect(apart.get(withProcedure)).toBe(modifierWords([procedure]))
    // The procedure word doesn't tell it from the row above; the video one does.
    expect(apart.get(withBoth)).toBe(modifierWords([video]))
    expect(apart.get(alone)).toBe('')
    // A modifier with no plain name is a code: it stays in Details.
    expect(apart.get(unnamed)).toBe('')
  })

  it('keeps the precise group line and the unit for Details', () => {
    expect(groupLine({ productLabel: 'Essential Plan', feeType: 'professional', settingLabel: "Doctor's office", modifiers: [] })).toBe(
      "Essential Plan · Doctor's fee · Doctor's office",
    )
    expect(unitSuffix({ perUnit: true })).toBe(' per unit')
    expect(unitSuffix({ perUnit: false })).toBe('')
  })
})
