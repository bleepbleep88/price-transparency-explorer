import { describe, expect, it } from 'vitest'
import type { OutOfNetworkFile } from '../../api/types'
import {
  OUT_OF_NETWORK_NOTES,
  PER_UNIT_NOTE,
  amountsListed,
  amountsRange,
  amountsSentence,
  amountsText,
  billedLine,
  billedVersusAllowed,
  fileNotes,
  groupLabel,
  leadGroup,
  listText,
  noBilledLine,
  outOfNetworkLine,
  outOfNetworkWhich,
  plansLensLine,
  providersLensLine,
  severalCodes,
  visibleGroups,
} from '../outOfNetworkText'
import { LISTED_CT_READ, oonGroup, oonResult as result } from './fixtures'

// No statistics words (decision 89), no "typical" figure for amounts that may cover several visits, and no "payments",
// which read as a claim count the files don't have.
const NOT_SAID = /median|percentile|quartile|÷|×|typical|payment/i

function file(productKey: string, productLabel: string, status: string): OutOfNetworkFile {
  return { fileId: 1, productKey, productLabel, url: `https://example.com/${productKey}.json`, status, fileLastUpdatedOn: null }
}

describe('out-of-network readings', () => {
  it('shows the amounts, never a typical one: their range from 5 providers, every amount under 5', () => {
    expect(amountsText(LISTED_CT_READ.allowed)).toBe('$218, $545, $763 and $8,374')
    expect(amountsText(oonGroup().allowed)).toBe('ran from $60.12 to $187,539')
    expect(amountsText({ count: 6, min: 300, median: 300, max: 300, listed: [] })).toBe('all $300')
    expect(amountsSentence(oonGroup().allowed, 'Allowed amounts')).toBe('Allowed amounts listed ran from $60.12 to $187,539.')
    expect(amountsSentence(LISTED_CT_READ.billed!, 'Billed charges')).toBe('Billed charges listed: $3,892, $9,730, $13,622 and $134,648.')
    expect(listText([1700])).toBe('$1,700')
    expect(listText([775.71, 936.22])).toBe('$776 and $936')
    expect(amountsListed(20)).toBe('20 different amounts listed')
    expect(amountsListed(1)).toBe('1 different amount listed')
  })

  it('says "times" only from exactly 2×, a percentage below, and "about the same" within 5%', () => {
    expect(billedVersusAllowed(oonGroup())).toBe('about 4.8 times')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 2 }))).toBe('about 2 times')
    // 1.97 is under 2: never rounded up to "about 2 times".
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 1.97 }))).toBe('about 97% more than')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 1.48 }))).toBe('about 48% more than')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 1.04 }))).toBe('about the same as')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 0.96 }))).toBe('about the same as')
    expect(billedVersusAllowed(oonGroup({ billedToAllowed: 0.8 }))).toBe('about 20% less than')
    expect(billedLine(oonGroup())).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(billedLine(oonGroup({ billedToAllowed: 1.97 }))).toBe('Providers usually billed about 97% more than what the plan allowed.')
  })

  it('counts providers, not amounts: under 5 providers nothing is compared', () => {
    expect(billedVersusAllowed(LISTED_CT_READ)).toBeNull()
    expect(billedVersusAllowed(oonGroup({ providers: 4 }))).toBeNull()
    expect(billedLine(LISTED_CT_READ)).toBeNull()
    expect(noBilledLine(LISTED_CT_READ)).toBe('Only 1 provider: too few to compare.')
    expect(noBilledLine(oonGroup({ billed: null, billedToAllowed: null }))).toBe('Too few billed charges published to compare.')
  })

  it('says only what the files support about the amounts, in short notes', () => {
    expect(OUT_OF_NETWORK_NOTES).toContain("The files don't say whether an amount covers one visit or several.")
    for (const note of [...OUT_OF_NETWORK_NOTES, PER_UNIT_NOTE]) {
      expect(note).not.toMatch(/look like|totals|so most|whole service/i)
      expect(note).not.toMatch(NOT_SAID)
    }
    for (const note of OUT_OF_NETWORK_NOTES) expect(note.split(' ').length).toBeLessThanOrEqual(14)
  })

  it('says in one line per product when its file was only partly read or could not be loaded', () => {
    const files = [
      file('ambetter-from-fidelis-care', 'Ambetter from Fidelis Care', 'failed'),
      file('essential-plan', 'Essential Plan', 'partial'),
      file('other', 'Other Plan', 'loaded'),
    ]
    const res = { ...result([oonGroup()]), files }
    expect(fileNotes(res, {})).toEqual([
      "Ambetter from Fidelis Care's out-of-network file couldn't be loaded, so its amounts aren't shown.",
      "Essential Plan's out-of-network file was only partly read, so some amounts may be missing.",
    ])
    expect(fileNotes(res, { product: 'essential-plan' })).toHaveLength(1)
    expect(fileNotes(result([oonGroup()]), {})).toEqual([])
    expect(fileNotes(null, {})).toEqual([])
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

  it('reads the lead group (most providers, then most amounts) for the lens lines, in plain words', () => {
    const groups = [LISTED_CT_READ, oonGroup()]
    expect(leadGroup(groups)?.code).toBe('0450')
    expect(leadGroup([oonGroup({ key: 'a', payments: 9 }), oonGroup({ key: 'b', payments: 21 })])?.key).toBe('b')
    const plans = plansLensLine(groups)
    const providers = providersLensLine([oonGroup()])
    expect(plans).toBe(
      'Out of network (Essential Plan · Facility fee · RC 0450), providers usually billed about 4.8 times what the plan allowed, across 20 different amounts listed.',
    )
    expect(providers).toBe(
      'Providers outside the network usually billed about 4.8 times what the plan allowed them (Essential Plan · Facility fee, 20 different amounts listed).',
    )
    expect(plansLensLine([oonGroup({ billedToAllowed: 1.97 })])).toContain('providers usually billed about 97% more than what the plan allowed')
    expect(`${plans} ${providers}`).not.toMatch(NOT_SAID)
  })

  it('lists the amounts under 5 providers, gives their range otherwise, and says nothing without groups', () => {
    expect(plansLensLine([LISTED_CT_READ])).toBe(
      "Out of network (Ambetter from Fidelis Care · Doctor's fee), the plan allowed $218, $545, $763 and $8,374 (1 provider).",
    )
    expect(providersLensLine([LISTED_CT_READ])).toBe(
      "Providers outside the network were allowed $218, $545, $763 and $8,374 (Ambetter from Fidelis Care · Doctor's fee, 1 provider).",
    )
    const noCharges = oonGroup({ billed: null, billedToAllowed: null })
    expect(plansLensLine([noCharges])).toBe(
      'Out of network (Essential Plan · Facility fee), the amounts the plan allowed ran from $60.12 to $187,539 (20 different amounts listed).',
    )
    expect(providersLensLine([noCharges])).toBe(
      'For providers outside the network, the amounts the plan allowed ran from $60.12 to $187,539 (Essential Plan · Facility fee, 20 different amounts listed).',
    )
    for (const line of [plansLensLine([LISTED_CT_READ]), providersLensLine([noCharges])]) expect(line).not.toMatch(NOT_SAID)
    expect(plansLensLine([])).toBeNull()
    expect(providersLensLine([])).toBeNull()
  })

  it('gives the panel one line for the bill with the most providers, never a code', () => {
    expect(outOfNetworkLine([LISTED_CT_READ, oonGroup()])).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(outOfNetworkLine([oonGroup({ billedToAllowed: 1.97 })])).toBe('Providers usually billed about 97% more than what the plan allowed.')
    // No billed charges to compare: what the plan allowed, as a range.
    expect(outOfNetworkLine([oonGroup({ billed: null, billedToAllowed: null })])).toBe('The plan allowed $60.12\u00a0–\u00a0$187,539.')
    // Under 5 providers: how few, and the amounts themselves.
    expect(outOfNetworkLine([LISTED_CT_READ])).toBe('Only 1 place: the plan allowed $218 · $545 · $763 · $8,374.')
    expect(outOfNetworkLine([])).toBeNull()
  })

  it('lists up to 4 amounts and gives a range past that', () => {
    expect(amountsRange(LISTED_CT_READ.allowed)).toBe('$218 · $545 · $763 · $8,374')
    expect(amountsRange({ count: 6, min: 23.38, median: null, max: 20767, listed: [23.38, 299, 513, 598, 1025, 20767] })).toBe('$23.38\u00a0–\u00a0$20,767')
    expect(amountsRange({ count: 6, min: 300, median: 300, max: 300, listed: [] })).toBe('$300')
  })

  it('says which bill the line is for only when the panel holds several, in plain words', () => {
    expect(outOfNetworkWhich([oonGroup()])).toBeNull()
    expect(outOfNetworkWhich([oonGroup(), LISTED_CT_READ])).toBe('Essential Plan · Facility fee')
    const doctor = oonGroup({ key: 'b', feeType: 'professional', providers: 9, modifiers: [{ code: 'LT', label: 'Left side (modifier LT)' }] })
    expect(outOfNetworkWhich([oonGroup(), doctor])).toBe("Doctor's fee · left side")
  })
})
