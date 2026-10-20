import { describe, expect, it } from 'vitest'
import {
  aboutTimes,
  amountHeadline,
  amountRange,
  amountSentence,
  billedLine,
  billedVersusAllowed,
  dollars,
  groupLabel,
  leadGroup,
  listText,
  plansLensLine,
  providersLensLine,
  severalCodes,
  visibleGroups,
} from '../outOfNetworkText'
import { LISTED_CT_READ, oonGroup, oonResult as result } from './fixtures'

const NO_STATISTICS_WORDS = /median|percentile|quartile|÷|×/i

describe('out-of-network readings', () => {
  it('writes dollars the way the rest of the app does: whole dollars from $100, cents below', () => {
    expect(dollars(1375.18)).toBe('$1,375')
    expect(dollars(60.12)).toBe('$60.12')
    expect(dollars(48)).toBe('$48')
    expect(aboutTimes(4.81)).toBe('about 4.8 times')
    expect(aboutTimes(33.33)).toBe('about 33 times')
  })

  it('lists fewer than 5 amounts and gives the typical amount and the range from 5 on', () => {
    expect(amountHeadline(LISTED_CT_READ.allowed)).toBe('$218, $545, $763 and $8,374')
    expect(amountRange(LISTED_CT_READ.allowed)).toBeNull()
    expect(amountHeadline(oonGroup().allowed)).toBe('Typical: $1,375')
    expect(amountRange(oonGroup().allowed)).toBe('Ran from $60.12 to $187,539')
    expect(listText([1700])).toBe('$1,700')
    expect(listText([775.71, 936.22])).toBe('$776 and $936')
    expect(amountSentence(oonGroup().allowed, 'allowed amount')).toBe('Typical allowed amount: $1,375. Ran from $60.12 to $187,539.')
    expect(amountSentence(LISTED_CT_READ.billed!, 'billed charge')).toBe('Billed charges: $3,892, $9,730, $13,622 and $134,648.')
  })

  it('says how billed charges compare with the allowed amount only with enough pairs', () => {
    expect(billedVersusAllowed(oonGroup())).toBe('about 4.8 times')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 1.01 }))).toBe('about the same as')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 0.8 }))).toBe('less than')
    expect(billedVersusAllowed(LISTED_CT_READ)).toBeNull()
    expect(billedLine(oonGroup())).toBe('Billed charges were usually about 4.8 times the allowed amount.')
    expect(billedLine(LISTED_CT_READ)).toBeNull()
  })

  it('applies the search page product and fee type filters', () => {
    const groups = [oonGroup(), LISTED_CT_READ]
    expect(visibleGroups(result(groups), {})).toHaveLength(2)
    expect(visibleGroups(result(groups), { product: 'essential-plan' }).map((g) => g.key)).toEqual(['essential-plan|RC|0450|facility|'])
    expect(visibleGroups(result(groups), { feeType: 'professional' }).map((g) => g.code)).toEqual(['74177'])
    expect(visibleGroups(null, {})).toEqual([])
  })

  it("names the doctor's or facility fee, the code only when one search matched several kinds, and modifiers as modifiers", () => {
    const anesthesia = oonGroup({ codeType: 'CPT', code: '00450' })
    expect(severalCodes([oonGroup(), anesthesia])).toBe(true)
    expect(severalCodes([oonGroup(), oonGroup({ productLabel: 'Ambetter from Fidelis Care' })])).toBe(false)
    expect(groupLabel(oonGroup())).toBe('Essential Plan · Facility fee')
    expect(groupLabel(LISTED_CT_READ)).toBe("Ambetter from Fidelis Care · Doctor's fee")
    expect(groupLabel(anesthesia, true)).toBe('Essential Plan · Facility fee · CPT 00450')
    expect(groupLabel(oonGroup({ modifiers: [{ code: '26', label: 'Professional component' }] }))).toBe(
      'Essential Plan · Facility fee · Professional component (modifier 26)',
    )
  })

  it('reads the lead group (most payments) for the lens lines, in plain words', () => {
    const groups = [LISTED_CT_READ, oonGroup()]
    expect(leadGroup(groups)?.code).toBe('0450')
    const plans = plansLensLine(groups)
    const providers = providersLensLine([oonGroup()])
    expect(plans).toBe(
      'Out of network (Essential Plan · Facility fee · RC 0450), billed charges were usually about 4.8 times what the plan allowed, over 20 payments.',
    )
    expect(providers).toBe(
      'Providers outside the network usually billed about 4.8 times what the plan allowed them (Essential Plan · Facility fee, 20 payments).',
    )
    expect(`${plans} ${providers}`).not.toMatch(NO_STATISTICS_WORDS)
  })

  it('falls back to the allowed amounts when billed charges cannot be compared, and says nothing without groups', () => {
    expect(plansLensLine([LISTED_CT_READ])).toBe(
      "Out of network (Ambetter from Fidelis Care · Doctor's fee), the plan allowed $218, $545, $763 and $8,374 over 4 payments.",
    )
    expect(providersLensLine([LISTED_CT_READ])).toBe(
      "Providers outside the network were allowed $218, $545, $763 and $8,374 (Ambetter from Fidelis Care · Doctor's fee).",
    )
    const noCharges = oonGroup({ billed: null, billedToAllowed: null })
    expect(plansLensLine([noCharges])).toBe('Out of network (Essential Plan · Facility fee), the typical allowed amount was $1,375 over 20 payments.')
    expect(providersLensLine([noCharges])).toBe(
      'For providers outside the network, the typical allowed amount was $1,375 (Essential Plan · Facility fee, 20 payments).',
    )
    expect(plansLensLine([])).toBeNull()
    expect(providersLensLine([])).toBeNull()
  })
})
