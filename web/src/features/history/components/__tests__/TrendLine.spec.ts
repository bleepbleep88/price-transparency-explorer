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

const yours = (rate: number) => ({ providerKey: '061036715', displayName: 'PM Pediatrics', rate })
const threeFiles = trend([
  point(3, '2026-07-27', { median: 113.01, provider: yours(100.36) }),
  point(2, '2026-08-31', { median: 113.59, provider: yours(100.36) }),
  point(1, '2026-09-28', { median: 113.59, provider: yours(117.77) }),
])

describe('TrendLine', () => {
  it('draws the group across the files and says the typical price in words', async () => {
    const line = await mountLine(threeFiles)

    expect(line.find('[data-testid="trend-chart-stub"]').exists()).toBe(true)
    expect(line.get('[data-testid="trend-typical"]').text()).toBe(
      'The typical price went from $113.01 (Jul 27) to $113.59 (Sep 28): up $0.58 (under 1%).',
    )
    expect(line.text().toLowerCase()).not.toMatch(/median|percentile|quartile/)
    expect(line.find('[data-testid="trend-provider"]').exists()).toBe(false)
  })

  it("follows the searched provider's rate when an NPI or EIN picked one out", async () => {
    const line = await mountLine(threeFiles, true)

    expect(line.get('[data-testid="trend-provider"]').text()).toBe(
      'Your rate went from $100.36 (Jul 27) to $117.77 (Sep 28): up $17.41 (17%).',
    )
  })

  it('shows nothing for a group too small for a typical price when no provider is followed', async () => {
    const small = trend([point(2, '2026-08-31', { median: null, statsLevel: 'list' }), point(1, '2026-09-28', { median: null, statsLevel: 'list' })])

    expect((await mountLine(small)).html()).not.toContain('trend-line')
  })

  it('says when no other loaded file has the group', async () => {
    const line = await mountLine(trend([point(1, '2026-09-28')]))

    expect(line.get('[data-testid="trend-only-here"]').text()).toBe('No other loaded file has this service in this setting and plan.')
  })

  it('leaves an incomplete file out and says so', async () => {
    const line = await mountLine(
      trend([point(3, '2026-07-27', { partial: true, median: 610 }), point(2, '2026-08-31', { median: 610 }), point(1, '2026-09-28', { median: 620 })]),
    )

    expect(line.get('[data-testid="trend-typical"]').text()).toBe('The typical price went from $610 (Aug 31) to $620 (Sep 28): up $10 (2%).')
    expect(line.text()).toContain("The Jul 27 file looks incomplete, so it's left out.")
  })
})
