import { describe, expect, it } from 'vitest'
import type { ScorecardGroup } from '../../api/types'
import { filterScorecardGroups, scorecardRowText } from '../scorecardFilter'

function group(overrides: Partial<ScorecardGroup> = {}): ScorecardGroup {
  return {
    groupKey: 'essential-plan|CPT|99213|facility|hospital-outpatient||negotiated',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'CPT',
    code: '99213',
    codeName: 'OFFICE/OUTPATIENT VISIT EST',
    categoryKey: 'visits',
    categoryLabel: 'Office, ER & hospital visits',
    feeType: 'facility',
    feeTypeLabel: 'Facility fee',
    settingKey: 'hospital-outpatient',
    settingLabel: 'Hospital outpatient department',
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per visit',
    perUnit: false,
    providerCount: 34,
    statsLevel: 'full',
    median: 324,
    value: 402.32,
    rateLow: 402.32,
    rateHigh: 402.32,
    percentile: 0.8,
    vsMedian: 1.24,
    band: 'high',
    facilityCharge: false,
    search: { code: '99213', codeType: 'CPT', group: 'essential-plan|CPT|99213|facility|hospital-outpatient||negotiated' },
    ...overrides,
  }
}

const office = group()
const er = group({
  groupKey: 'ambetter-from-fidelis-care|CPT|99285|facility|er||negotiated',
  productKey: 'ambetter-from-fidelis-care',
  productLabel: 'Ambetter from Fidelis Care',
  code: '99285',
  codeName: 'EMERGENCY DEPT VISIT',
  settingKey: 'er',
  settingLabel: 'Hospital emergency room',
})
const operatingRoom = group({ codeType: 'RC', code: '0360', codeName: null, categoryLabel: 'Facility charges', facilityCharge: true, unit: 'per facility charge' })
const rows = [office, er, operatingRoom]

describe('the scorecard filter', () => {
  it('finds rows by code, name, plan, setting, fee and category, in any case', () => {
    expect(filterScorecardGroups(rows, '99213')).toEqual([office])
    expect(filterScorecardGroups(rows, 'emergency')).toEqual([er])
    expect(filterScorecardGroups(rows, 'AMBETTER')).toEqual([er])
    expect(filterScorecardGroups(rows, 'hospital outpatient')).toEqual([office, operatingRoom])
    expect(filterScorecardGroups(rows, 'facility fee')).toEqual(rows)
    expect(filterScorecardGroups(rows, 'facility-only')).toEqual([operatingRoom])
  })

  it('finds rows by the plain names the page shows', () => {
    // 99213 reads "Office visit, level 3"; the ER bill reads "Emergency room"; Ambetter's plan reads "Ambetter".
    expect(filterScorecardGroups(rows, 'level 3')).toEqual([office])
    expect(filterScorecardGroups(rows, 'emergency room')).toEqual([er])
    expect(filterScorecardGroups(rows, 'facility charge')).toEqual([operatingRoom])
  })

  it('needs every word, in any order', () => {
    expect(filterScorecardGroups(rows, 'outpatient visit')).toEqual([office])
    // Both visits sit in the category "Office, ER & hospital visits".
    expect(filterScorecardGroups(rows, 'office visit')).toEqual([office, er])
    expect(filterScorecardGroups(rows, 'ambetter emergency')).toEqual([er])
    expect(filterScorecardGroups(rows, 'emergency essential')).toEqual([])
  })

  it('keeps every row when the filter is empty', () => {
    expect(filterScorecardGroups(rows, '')).toEqual(rows)
    expect(filterScorecardGroups(rows, '   ')).toEqual(rows)
    expect(filterScorecardGroups(rows, null)).toEqual(rows)
  })

  it('reads what the row shows', () => {
    expect(scorecardRowText(er)).toContain('CPT 99285 · EMERGENCY DEPT VISIT')
    expect(scorecardRowText(er)).toContain('Ambetter from Fidelis Care · Facility fee · Hospital emergency room')
  })
})
