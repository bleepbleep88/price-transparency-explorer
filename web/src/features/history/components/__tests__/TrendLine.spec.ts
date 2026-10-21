import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { GroupTrend } from '../../api/types'
import { point, trend } from '../../lib/__tests__/fixtures'
import TrendLine from '../TrendLine.vue'

// The chart itself is ECharts (loaded on demand); here it only has to appear when there's a line to draw.
vi.mock('../TrendChart.vue', () => ({ __esModule: true, default: { name: 'TrendChart', template: '<div data-testid="trend-chart-stub" />' } }))

let wrapper: VueWrapper | undefined

async function mountLine(groupTrend: GroupTrend, showProvider = false) {
  wrapper = mount(TrendLine, {
    props: { trend: groupTrend, selectedId: 1, showProvider },
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } })] },
  })
  await flushPromises()
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

const yours = (rate: number) => ({ providerKey: '061036715', displayName: 'PM Pediatrics, PLLC', rate })
const threeFiles = trend([
  point(3, '2026-07-27', { median: 113.01, provider: yours(100.36) }),
  point(2, '2026-08-31', { median: 113.59, provider: yours(100.36) }),
  point(1, '2026-09-28', { median: 113.59, provider: yours(117.77) }),
])

describe('TrendLine', () => {
  it('draws the months and gives the typical price by month in a few words', async () => {
    const line = await mountLine(threeFiles)

    expect(line.find('[data-testid="trend-chart-stub"]').exists()).toBe(true)
    expect(line.get('[data-testid="trend-typical"]').text()).toBe('Typical: Jul $113 · Aug $114 · Sep $114')
    expect(line.text().toLowerCase()).not.toMatch(/median|percentile|quartile|card|snapshot/)
    expect(line.find('[data-testid="trend-provider"]').exists()).toBe(false)
  })

  it("follows the searched provider's rate by its plain name when an NPI or EIN picked one out", async () => {
    const line = await mountLine(threeFiles, true)

    expect(line.get('[data-testid="trend-provider"]').text()).toBe('PM Pediatrics: Jul $100.36 · Aug $100.36 · Sep $117.77')
  })

  it('says who stayed and who left when only who is listed moved the price', async () => {
    const line = await mountLine(
      trend([
        point(2, '2026-08-31', { median: 51.45 }),
        point(1, '2026-09-28', {
          median: 113.59,
          sincePrevious: { sinceSnapshotId: 2, stayed: 37, raised: 0, lowered: 0, reweighted: 0, left: 87, joined: 39 },
        }),
      ]),
    )

    expect(line.get('[data-testid="trend-who"]').text()).toBe('Aug–Sep: 37 kept their rate, 87 left, 39 joined')
    expect(line.text()).not.toMatch(/went up|times as much/)
  })

  it('shows nothing for a group too small for a typical price when no provider is followed', async () => {
    const small = trend([point(2, '2026-08-31', { median: null, statsLevel: 'list' }), point(1, '2026-09-28', { median: null, statsLevel: 'list' })])

    expect((await mountLine(small)).html()).not.toContain('trend-line')
  })

  it('says when no other month has the prices', async () => {
    const line = await mountLine(trend([point(1, '2026-09-28')]))

    expect(line.get('[data-testid="trend-only-here"]').text()).toBe('No other month has these prices.')
  })

  it('leaves an incomplete file out and says so', async () => {
    const line = await mountLine(
      trend([point(3, '2026-07-27', { partial: true, median: 610 }), point(2, '2026-08-31', { median: 610 }), point(1, '2026-09-28', { median: 620 })]),
    )

    expect(line.get('[data-testid="trend-typical"]').text()).toBe('Typical: Aug $610 · Sep $620')
    expect(line.text()).toContain('Jul file incomplete, left out')
  })
})
