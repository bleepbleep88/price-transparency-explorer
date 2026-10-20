import { describe, expect, it } from 'vitest'
import type { ProviderScorecard, StatisticsBasis } from '../../api/types'
import { overallSummary, positionCounts, serviceName, share, staleMessage, statisticsLine } from '../insightText'

function index(overrides: Partial<ProviderScorecard['index']> = {}): ProviderScorecard['index'] {
  return { value: 1.08, groupsCounted: 26, groupsTotal: 26, minimumGroups: 5, minimumProviders: 5, below: 11, atMedian: 1, above: 14, ...overrides }
}

function basis(overrides: Partial<StatisticsBasis> = {}): StatisticsBasis {
  return { snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true }, builtAt: '2026-10-20T08:42:16Z', isCurrent: true, ...overrides }
}

describe('overallSummary', () => {
  it('says how the provider usually compares, and across how many services', () => {
    expect(overallSummary(index())).toEqual({
      headline: 'Usually about 8% more than typical',
      detail: 'Across 26 services with 5 or more providers.',
    })
    expect(overallSummary(index({ value: 0.88 })).headline).toBe('Usually about 12% less than typical')
    expect(overallSummary(index({ value: 1.03 })).headline).toBe('About typical')
  })

  it('says why there is no summary', () => {
    expect(overallSummary(index({ value: null, groupsCounted: 4 }))).toEqual({
      headline: 'Not enough to sum up',
      detail: 'That needs 5 services with 5 or more providers each; this provider has 4.',
    })
  })
})

describe('positionCounts', () => {
  it('counts where its rates sit, leaving out groups too small to compare', () => {
    expect(positionCounts([{ band: 'low' }, { band: 'high' }, { band: 'typical' }, { band: 'high' }, { band: null }])).toBe(
      'Lower than most for 1, about typical for 1, higher than most for 2.',
    )
    expect(positionCounts([{ band: null }])).toBeNull()
  })
})

describe('figures line', () => {
  it('names the plan files and when their figures were saved', () => {
    expect(statisticsLine(basis())).toBe('Plan files of Aug 31, 2026 · figures saved Oct 20, 2026')
    expect(statisticsLine(basis({ builtAt: null }))).toBe('Plan files of Aug 31, 2026')
  })

  it('warns only when the figures are not current', () => {
    expect(staleMessage(basis())).toBeNull()
    expect(staleMessage(basis({ isCurrent: false }))).toContain('out of date')
    expect(staleMessage(basis({ isCurrent: false, builtAt: null }))).toContain("haven't been saved yet")
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
  })
})
