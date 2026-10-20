import { describe, expect, it } from 'vitest'
import type { ProviderScorecard, StatisticsBasis } from '../../api/types'
import { OVERALL_EXPLAINER, overallSummary, serviceName, share, staleMessage, statisticsLine } from '../insightText'

function scorecard(index: Partial<ProviderScorecard['index']> = {}, facilityCharges = 0, includeFacilityCharges = false) {
  return {
    index: { value: 1.08, groupsCounted: 26, groupsTotal: 26, minimumGroups: 5, minimumProviders: 5, below: 11, atMedian: 1, above: 14, ...index },
    facilityCharges,
    includeFacilityCharges,
  }
}

function basis(overrides: Partial<StatisticsBasis> = {}): StatisticsBasis {
  return { snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true }, builtAt: '2026-10-20T08:42:16Z', isCurrent: true, ...overrides }
}

describe('overallSummary', () => {
  it('says how the provider usually compares, across how many services', () => {
    expect(overallSummary(scorecard())).toEqual({ headline: 'Usually about 8% more than typical (across 26 services)', detail: null })
    expect(overallSummary(scorecard({ value: 0.88 })).headline).toBe('Usually about 12% less than typical (across 26 services)')
    expect(overallSummary(scorecard({ value: 1.03 })).headline).toBe('About typical (across 26 services)')
  })

  it('says why there is no reading, and that facility charges were left out', () => {
    expect(overallSummary(scorecard({ value: null, groupsCounted: 3 }, 74))).toEqual({
      headline: 'Not enough to sum up',
      detail: "That needs 5 services with 5 or more providers each; this provider has 3. Its 74 facility charges with no procedure named aren't counted.",
    })
    expect(overallSummary(scorecard({}, 1)).detail).toBe("Its 1 facility charge with no procedure named isn't counted.")
    expect(overallSummary(scorecard({}, 5, true)).detail).toBeNull()
  })

  it("explains itself the same way whichever way the provider leans", () => {
    expect(OVERALL_EXPLAINER).toContain('this is the one in the middle')
    expect(OVERALL_EXPLAINER).not.toMatch(/median|percentile|half/i)
  })
})

describe('figures line', () => {
  it('names the plan files and when their figures were saved', () => {
    expect(statisticsLine(basis())).toBe('Plan files of Aug 31, 2026 · figures saved Oct 20, 2026')
    expect(statisticsLine(basis({ builtAt: null }))).toBe('Plan files of Aug 31, 2026')
  })

  it('promises a refresh only while a load or rebuild is running', () => {
    expect(staleMessage(basis(), true)).toBeNull()
    expect(staleMessage(basis({ isCurrent: false }), true)).toContain('This page refreshes when that finishes.')
    expect(staleMessage(basis({ isCurrent: false }), false)).toContain("They'll be rebuilt the next time the data loads.")
    expect(staleMessage(basis({ isCurrent: false, builtAt: null }), true)).toContain('This page fills in when they are.')
    expect(staleMessage(basis({ isCurrent: false, builtAt: null }), false)).toContain("They'll be saved the next time the data loads.")
  })
})

describe('serviceName and share', () => {
  it('names a code with its published name when there is one', () => {
    expect(serviceName({ codeType: 'CPT', code: '45378', codeName: 'Colonoscopy' })).toBe('CPT 45378 · Colonoscopy')
    expect(serviceName({ codeType: 'RC', code: '0360', codeName: null })).toBe('RC 0360')
  })

  it('rounds a share to a whole percent', () => {
    expect(share(5985, 9369)).toBe('64%')
    expect(share(0, 0)).toBe('—')
    expect(share(0, 7951)).toBe('0%')
  })

  it('never rounds a few to 0% or nearly all to 100%', () => {
    expect(share(17, 7951)).toBe('under 1%')
    expect(share(7949, 7951)).toBe('over 99%')
    expect(share(71, 71)).toBe('100%')
  })
})
