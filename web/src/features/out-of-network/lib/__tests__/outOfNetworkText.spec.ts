import { describe, expect, it } from 'vitest'
import {
  amountHeadline,
  amountRange,
  groupLabel,
  leadGroup,
  listText,
  percent,
  plansLensLine,
  providersLensLine,
  severalCodes,
  visibleGroups,
} from '../outOfNetworkText'
import { LISTED_CT_READ, oonGroup, oonResult as result } from './fixtures'

describe('out-of-network readings', () => {
  it('lists fewer than 5 amounts and summarizes 5 or more', () => {
    expect(amountHeadline(LISTED_CT_READ.allowed)).toBe('$217.92, $544.80, $762.72 and $8,373.66')
    expect(amountRange(LISTED_CT_READ.allowed)).toBeNull()
    expect(amountHeadline(oonGroup().allowed)).toBe('median $1,375.18')
    expect(amountRange(oonGroup().allowed)).toBe('$60.12 to $187,538.55')
    expect(listText([1700])).toBe('$1,700.00')
    expect(listText([775.71, 936.22])).toBe('$775.71 and $936.22')
  })

  it('applies the search page product and fee type filters', () => {
    const groups = [oonGroup(), LISTED_CT_READ]
    expect(visibleGroups(result(groups), {})).toHaveLength(2)
    expect(visibleGroups(result(groups), { product: 'essential-plan' }).map((g) => g.key)).toEqual(['essential-plan|RC|0450|facility|'])
    expect(visibleGroups(result(groups), { feeType: 'professional' }).map((g) => g.code)).toEqual(['74177'])
    expect(visibleGroups(null, {})).toEqual([])
  })

  it('names the code only when one search matched several kinds of code, and names modifiers as modifiers', () => {
    const anesthesia = oonGroup({ codeType: 'CPT', code: '00450' })
    expect(severalCodes([oonGroup(), anesthesia])).toBe(true)
    expect(severalCodes([oonGroup(), oonGroup({ productLabel: 'Ambetter from Fidelis Care' })])).toBe(false)
    expect(groupLabel(oonGroup())).toBe('Essential Plan · Facility fee')
    expect(groupLabel(anesthesia, true)).toBe('Essential Plan · Facility fee · CPT 00450')
    expect(groupLabel(oonGroup({ modifiers: [{ code: '26', label: 'Professional component' }] }))).toBe(
      'Essential Plan · Facility fee · Professional component (modifier 26)',
    )
  })

  it('reads the lead group (most payments) for the lens lines, as a share when there are enough pairs', () => {
    const groups = [LISTED_CT_READ, oonGroup()]
    expect(leadGroup(groups)?.code).toBe('0450')
    expect(percent(0.21)).toBe('21%')
    expect(plansLensLine(groups)).toBe(
      'Out of network (Essential Plan · Facility fee · RC 0450), the plan allowed a median 21% of what providers billed, over 20 payments.',
    )
    expect(providersLensLine([oonGroup()])).toBe(
      'Providers outside the network were allowed a median 21% of what they billed (Essential Plan · Facility fee, 20 payments).',
    )
  })

  it('lists the amounts in the lens lines when there are too few pairs for a share, and says nothing without groups', () => {
    expect(plansLensLine([LISTED_CT_READ])).toBe(
      'Out of network (Ambetter from Fidelis Care · Professional (clinician) fee), the plan listed 4 payments: $217.92, $544.80, $762.72 and $8,373.66.',
    )
    expect(providersLensLine([LISTED_CT_READ])).toBe(
      'Providers outside the network were allowed $217.92, $544.80, $762.72 and $8,373.66 (Ambetter from Fidelis Care · Professional (clinician) fee).',
    )
    expect(plansLensLine([])).toBeNull()
    expect(providersLensLine([])).toBeNull()
  })
})
