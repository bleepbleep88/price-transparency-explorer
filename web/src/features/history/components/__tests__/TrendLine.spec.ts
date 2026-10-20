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
      'The typical price was $113 (Jul 27), $114 (Aug 31) and $114 (Sep 28): up $1 (under 1%) since Jul 27.',
    )
    expect(line.text().toLowerCase()).not.toMatch(/median|percentile|quartile/)
    expect(line.find('[data-testid="trend-provider"]').exists()).toBe(false)
  })

  it("follows the searched provider's rate by name when an NPI or EIN picked one out", async () => {
    const line = await mountLine(threeFiles, true)

    // A card is read by every audience, so it isn't "your rate".
    expect(line.get('[data-testid="trend-provider"]').text()).toBe(
      'The rate for PM Pediatrics was $100.36 (Jul 27), $100.36 (Aug 31) and $117.77 (Sep 28): up $17.41 (17%) since Jul 27.',
    )
  })

  it('says why the typical price moved when the providers in both files kept their rates', async () => {
    const line = await mountLine(
      trend([
        point(2, '2026-08-31', { median: 573.48 }),
        point(1, '2026-09-28', {
          median: 610,
          sincePrevious: { sinceSnapshotId: 2, stayed: 40, raised: 0, lowered: 0, left: 87, joined: 39 },
        }),
      ]),
    )

    expect(line.get('[data-testid="trend-typical"]').text()).toBe(
      'The typical price went from $573 (Aug 31) to $610 (Sep 28): up $37 (6%). Providers listed in both files kept their rates: the typical price moved because different providers are listed (87 left and 39 joined).',
    )
  })

  it('shows nothing for a group too small for a typical price when no provider is followed', async () => {
    const small = trend([point(2, '2026-08-31', { median: null, statsLevel: 'list' }), point(1, '2026-09-28', { median: null, statsLevel: 'list' })])

    expect((await mountLine(small)).html()).not.toContain('trend-line')
  })

  it('says when no other loaded file has the group', async () => {
    const line = await mountLine(trend([point(1, '2026-09-28')]))

    expect(line.get('[data-testid="trend-only-here"]').text()).toBe('No other loaded file has prices for this card.')
  })

  it('leaves an incomplete file out and says so', async () => {
    const line = await mountLine(
      trend([point(3, '2026-07-27', { partial: true, median: 610 }), point(2, '2026-08-31', { median: 610 }), point(1, '2026-09-28', { median: 620 })]),
    )

    expect(line.get('[data-testid="trend-typical"]').text()).toBe('The typical price went from $610 (Aug 31) to $620 (Sep 28): up $10 (2%).')
    expect(line.text()).toContain("The Jul 27 file looks incomplete, so it's left out.")
  })
})
