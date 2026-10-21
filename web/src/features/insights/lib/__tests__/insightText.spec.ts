import { describe, expect, it } from 'vitest'
import type { ProviderScorecard, StatisticsBasis } from '../../api/types'
import { facilityLeftOut, OVERALL_METHOD, overallReading, serviceName, share, staleMessage, statisticsLine, toneOf } from '../insightText'

function index(overrides: Partial<ProviderScorecard['index']> = {}): Pick<ProviderScorecard, 'index'> {
  return { index: { value: 1.18, groupsCounted: 26, groupsTotal: 26, minimumGroups: 5, minimumProviders: 5, below: 11, atMedian: 1, above: 14, ...overrides } }
}

function basis(overrides: Partial<StatisticsBasis> = {}): StatisticsBasis {
  return { snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true }, builtAt: '2026-10-20T08:42:16Z', isCurrent: true, ...overrides }
}

describe('overallReading', () => {
  it('says how the provider usually compares, big, and across how many services', () => {
    expect(overallReading(index())).toEqual({ figure: '18% above typical', line: 'Across 26 services', tone: 'high' })
    expect(overallReading(index({ value: 0.88 }))).toMatchObject({ figure: '12% below typical', tone: 'low' })
    expect(overallReading(index({ value: 1.03 }))).toMatchObject({ figure: 'About typical', tone: 'neutral' })
    expect(overallReading(index({ value: 2.4 })).figure).toBe('About 2.4 times typical')
  })

  it('says when there is too little to sum up, and what it takes', () => {
    expect(overallReading(index({ value: null, groupsCounted: 3 }))).toEqual({
      figure: 'Too few to say',
      line: 'Needs 5 services with 5+ places',
      tone: 'neutral',
    })
  })

  it('explains itself in Details the same way whichever way the provider leans', () => {
    expect(OVERALL_METHOD).toContain('the middle of those comparisons')
    expect(OVERALL_METHOD).not.toMatch(/median|percentile|half/i)
  })
})

describe('toneOf', () => {
  it('is green below typical, red above, plain within 5%', () => {
    expect(toneOf(0.9)).toBe('low')
    expect(toneOf(1.2)).toBe('high')
    expect(toneOf(1.02)).toBe('neutral')
    expect(toneOf(null)).toBe('neutral')
  })
})

describe('facilityLeftOut', () => {
  it('counts the facility-only charges left out of the reading', () => {
    expect(facilityLeftOut({ facilityCharges: 74, includeFacilityCharges: false })).toBe('74 facility-only charges not counted.')
    expect(facilityLeftOut({ facilityCharges: 74, includeFacilityCharges: true })).toBeNull()
    expect(facilityLeftOut({ facilityCharges: 0, includeFacilityCharges: false })).toBeNull()
  })
})

describe('figures line and stale note', () => {
  it('names the plan files and when their figures were saved', () => {
    expect(statisticsLine(basis())).toBe('Plan files of Aug 31, 2026 · figures saved Oct 20, 2026')
    expect(statisticsLine(basis({ builtAt: null }))).toBe('Plan files of Aug 31, 2026')
  })

  it('promises a refresh only while a load or rebuild is running, in ten words or fewer', () => {
    const lines = [
      staleMessage(basis({ isCurrent: false }), true),
      staleMessage(basis({ isCurrent: false }), false),
      staleMessage(basis({ isCurrent: false, builtAt: null }), true),
      staleMessage(basis({ isCurrent: false, builtAt: null }), false),
    ]
    expect(staleMessage(basis(), true)).toBeNull()
    expect(lines[0]).toContain('refreshes when done')
    expect(lines[1]).not.toContain('refreshes')
    expect(lines[2]).toContain('fills in when done')
    expect(lines[3]).toContain('next load')
    for (const line of lines) expect(line!.split(' ').length).toBeLessThanOrEqual(10)
  })
})

describe('serviceName and share', () => {
  it('names a code with its published name, for Details', () => {
    expect(serviceName({ codeType: 'CPT', code: '45378', codeName: 'Colonoscopy' })).toBe('CPT 45378 · Colonoscopy')
    expect(serviceName({ codeType: 'RC', code: '0360', codeName: null })).toBe('RC 0360')
  })

  it('rounds a share to a whole percent, never a few to 0% or nearly all to 100%', () => {
    expect(share(5985, 9369)).toBe('64%')
    expect(share(0, 0)).toBe('—')
    expect(share(17, 7951)).toBe('under 1%')
    expect(share(7949, 7951)).toBe('over 99%')
    expect(share(71, 71)).toBe('100%')
  })
})
