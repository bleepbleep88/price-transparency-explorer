import { afterEach, describe, expect, it, vi } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import { notifyManager } from '@tanstack/vue-query'
import type { ProviderPosition, RateGroup, RateSearchResult, SnapshotSummary } from '@/features/rates/api/types'
import { parseRouteQuery } from '@/features/rates/lib/searchParams'
import type { ChangesResult, RateTrendResult } from '../../api/types'
import type { ChangesParams } from '../../lib/changesParams'
import { point, SNAPSHOTS, trend } from '../../lib/__tests__/fixtures'
import LensHistoryLine from '../LensHistoryLine.vue'
import { mountWith } from './mountWith'

const snapshots = vi.hoisted(() => vi.fn<() => Promise<SnapshotSummary[]>>())
const changes = vi.hoisted(() => vi.fn<(params: ChangesParams) => Promise<ChangesResult>>())
vi.mock('@/features/rates/api/ratesApi', () => ({ ratesApi: { snapshots } }))
vi.mock('../../api/historyApi', () => ({ historyApi: { changes }, toTrendApiQuery: (params: object) => params }))

notifyManager.setScheduler(queueMicrotask)

const OFFICE = 'essential-plan|CPT|99213|professional|office|25|negotiated'
const ASC = 'essential-plan|CPT|99213|professional|asc||negotiated'

function group(key: string, providerCount: number, highlights: ProviderPosition[] = []): RateGroup {
  return {
    key,
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    codeType: 'CPT',
    code: '99213',
    feeType: 'professional',
    feeTypeLabel: 'Professional (clinician) fee',
    settingKey: key.includes('office') ? 'office' : 'asc',
    settingLabel: key.includes('office') ? "Doctor's office" : 'Ambulatory surgery center',
    settingNote: null,
    places: [],
    modifiers: [],
    negotiatedType: 'negotiated',
    unit: 'per visit',
    perUnit: false,
    billedAs: [],
    providerCount,
    rateCount: providerCount,
    statsLevel: 'full',
    stats: { min: 66.14, p10: 70, p25: 89.36, median: 113.59, p75: 113.59, p90: 150, max: 231.3, spreadMultiple: 3.5, p90ToP10: 1.36 },
    lowerQuartileGap: 24.23,
    listedRates: [],
    matchedProviders: highlights.length,
    highlights,
    notes: [],
  }
}

const PM = { providerKey: '061036715', displayName: 'PM Pediatrics', rate: 117.77, percentile: 95, vsMedian: 1.04, aboveP25: 28.41, rateLow: 117.77, rateHigh: 117.77 }

function result(groups: RateGroup[], ein: string | null = null): RateSearchResult {
  return {
    query: { code: '99213', matchedCodes: ['99213'], codeType: null, npi: null, ein, product: null, setting: null, feeType: null, provider: null, group: null, sort: 'rate', page: 1, pageSize: 25 },
    snapshot: { snapshotId: 1, indexDate: '2026-09-28', isLatest: true },
    code: null,
    groups,
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

const yours = (rate: number) => ({ providerKey: '061036715', displayName: 'PM Pediatrics', rate })
const TREND: RateTrendResult = {
  selected: { snapshotId: 1, indexDate: '2026-09-28', isLatest: true },
  snapshots: [
    { snapshotId: 3, indexDate: '2026-07-27', isLatest: false },
    { snapshotId: 2, indexDate: '2026-08-31', isLatest: false },
    { snapshotId: 1, indexDate: '2026-09-28', isLatest: true },
  ],
  groups: [
    trend(
      [
        point(3, '2026-07-27', { median: 113.01, provider: yours(100.36) }),
        point(2, '2026-08-31', { median: 113.59, provider: yours(100.36) }),
        point(1, '2026-09-28', { median: 113.59, provider: yours(117.77) }),
      ],
      OFFICE,
    ),
    trend([point(3, '2026-07-27', { median: 70 }), point(1, '2026-09-28', { median: 77 })], ASC),
  ],
}

let wrapper: VueWrapper | undefined

async function mountLine(lens: string, searchResult: RateSearchResult, query: Record<string, string> = {}) {
  snapshots.mockResolvedValue(SNAPSHOTS)
  const mounted = await mountWith(
    LensHistoryLine,
    { lens, result: searchResult, params: parseRouteQuery({ code: '99213', ...query }), trend: TREND },
    { code: '99213', ...query },
  )
  wrapper = mounted.wrapper
  return mounted
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  snapshots.mockReset()
  changes.mockReset()
})

describe('LensHistoryLine', () => {
  it("gives providers their own rate over time and a way to the card's trend", async () => {
    await mountLine('providers', result([group(ASC, 200), group(OFFICE, 114, [PM])], '061036715'), { ein: '061036715' })

    expect(wrapper!.get('[data-testid="lens-history"]').text()).toContain(
      'Your rate over time: $100.36 (Jul 27) → $100.36 (Aug 31) → $117.77 (Sep 28), +17.3% since Jul 27.',
    )
    await wrapper!.get('[data-testid="show-trend"]').trigger('click')
    expect(wrapper!.emitted('showTrend')).toEqual([[OFFICE]])
  })

  it('says nothing to providers until an NPI or EIN picks one out', async () => {
    await mountLine('providers', result([group(OFFICE, 114)]))

    expect(wrapper!.find('[data-testid="lens-history"]').exists()).toBe(false)
  })

  it("gives employers the largest group's median over time", async () => {
    await mountLine('employers', result([group(OFFICE, 114), group(ASC, 200)]))

    expect(wrapper!.get('[data-testid="lens-history"]').text()).toContain('Median over time, largest group: $70.00 (Jul 27) → $77.00 (Sep 28), +10.0% since Jul 27.')
    await wrapper!.get('[data-testid="show-trend"]').trigger('click')
    expect(wrapper!.emitted('showTrend')).toEqual([[ASC]])
  })

  it("tells plans what changed for the code since the last file, and links to those changes", async () => {
    changes.mockResolvedValue({
      current: true,
      counts: { changed: 113, increased: 56, decreased: 57, new: 683, newListedElsewhere: 229, dropped: 618, droppedListedElsewhere: 231, unchanged: 2136 },
    } as ChangesResult)

    await mountLine('plans', result([group(OFFICE, 114)]))

    expect(changes).toHaveBeenCalledWith(expect.objectContaining({ code: '99213', snapshot: undefined, pageSize: 1 }), expect.anything())
    const line = wrapper!.get('[data-testid="lens-history"]')
    expect(line.text()).toContain('Since the Aug 31 file, for 99213: 113 rates changed (56 up, 57 down), 683 new and 618 dropped.')
    expect(line.get('[data-testid="see-changes"]').attributes('href')).toBe('/changes?code=99213')
  })

  it("doesn't show counts from saved statistics that are out of date as current", async () => {
    changes.mockResolvedValue({
      current: false,
      counts: { changed: 5, increased: 5, decreased: 0, new: 0, newListedElsewhere: 0, dropped: 0, droppedListedElsewhere: 0, unchanged: 9 },
    } as ChangesResult)

    await mountLine('plans', result([group(OFFICE, 114)]))

    expect(wrapper!.get('[data-testid="lens-history"]').text()).toContain('The changes since the Aug 31 file are being updated.')
    expect(wrapper!.text()).not.toContain('5 rates changed')
  })

  it('compares an older snapshot with the file before it, and the oldest with nothing', async () => {
    changes.mockResolvedValue({
      current: true,
      counts: { changed: 0, increased: 0, decreased: 0, new: 0, newListedElsewhere: 0, dropped: 0, droppedListedElsewhere: 0, unchanged: 9 },
    } as ChangesResult)

    await mountLine('plans', result([group(OFFICE, 114)]), { snapshot: '2' })
    expect(changes).toHaveBeenCalledWith(expect.objectContaining({ snapshot: 2 }), expect.anything())
    expect(wrapper!.get('[data-testid="lens-history"]').text()).toContain('No rate for 99213 changed since the Jul 27 file.')
    expect(wrapper!.get('[data-testid="see-changes"]').attributes('href')).toBe('/changes?snapshot=2&code=99213')
    wrapper!.unmount()

    changes.mockClear()
    await mountLine('plans', result([group(OFFICE, 114)]), { snapshot: '3' })
    expect(changes).not.toHaveBeenCalled()
    expect(wrapper!.find('[data-testid="lens-history"]').exists()).toBe(false)
  })
})
