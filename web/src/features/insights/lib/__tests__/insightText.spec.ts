import { describe, expect, it } from 'vitest'
import type { ProviderScorecard, StatisticsBasis } from '../../api/types'
import { indexExplainer, indexSentence, medianCounts, percentDifference, serviceName, share, staleMessage, statisticsLine } from '../insightText'

function index(overrides: Partial<ProviderScorecard['index']> = {}): ProviderScorecard['index'] {
  return { value: 0.87, groupsCounted: 54, groupsTotal: 55, minimumGroups: 5, minimumProviders: 5, below: 36, atMedian: 1, above: 17, ...overrides }
}

function basis(overrides: Partial<StatisticsBasis> = {}): StatisticsBasis {
  return { snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true }, builtAt: '2026-10-20T08:42:16Z', isCurrent: true, ...overrides }
}

describe('percentDifference', () => {
  it('says how far a ratio is from the same rate, in whole percent', () => {
    expect(percentDifference(0.87)).toBe('13% lower')
    expect(percentDifference(1.1)).toBe('10% higher')
    expect(percentDifference(1)).toBe('the same')
    expect(percentDifference(25.13)).toBe('2,413% higher')
  })

  it('never calls a different rate the same', () => {
    expect(percentDifference(1.004)).toBe('within 1%')
  })
})

describe('indexSentence', () => {
  it('reads the index against the median', () => {
    expect(indexSentence(index())).toBe('Typically 13% below the median for the same service, across 54 groups with 5 or more providers.')
    expect(indexSentence(index({ value: 1.08 }))).toBe('Typically 8% above the median for the same service, across 54 groups with 5 or more providers.')
    expect(indexSentence(index({ value: 1, groupsCounted: 1 }))).toContain('Typically at the median for the same service, across 1 group')
  })

  it('says why there is no index', () => {
    expect(indexSentence(index({ value: null, groupsCounted: 4 }))).toBe(
      'No index: it needs 5 groups with 5 or more providers, and this provider has 4.',
    )
  })

  it('explains itself and counts where the rates sit', () => {
    expect(indexExplainer(0.87)).toContain('Half its groups sit at or below 0.87×, half at or above.')
    expect(indexExplainer(null)).not.toContain('Half')
    expect(medianCounts(index())).toBe('Below the median in 36 groups, at it in 1, above it in 17.')
  })
})

describe('statistics basis', () => {
  it('names the snapshot and when its statistics were saved', () => {
    expect(statisticsLine(basis())).toBe('Snapshot Aug 31, 2026 · statistics saved Oct 20, 2026')
    expect(statisticsLine(basis({ builtAt: null }))).toBe('Snapshot Aug 31, 2026')
  })

  it('warns only when the statistics are not current', () => {
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
