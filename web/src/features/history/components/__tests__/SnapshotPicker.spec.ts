import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, type VueWrapper } from '@vue/test-utils'
import type { Router } from 'vue-router'
import { notifyManager } from '@tanstack/vue-query'
import type { SnapshotSummary } from '@/features/rates/api/types'
import { product, snapshot, SNAPSHOTS } from '../../lib/__tests__/fixtures'
import SnapshotPicker from '../SnapshotPicker.vue'
import { mountWith } from './mountWith'

const snapshots = vi.hoisted(() => vi.fn<() => Promise<SnapshotSummary[]>>())
vi.mock('@/features/rates/api/ratesApi', () => ({ ratesApi: { snapshots } }))

// Query results reach the component as microtasks, so flushPromises settles them (no timers).
notifyManager.setScheduler(queueMicrotask)

let wrapper: VueWrapper | undefined

async function mountPicker(list: SnapshotSummary[], query: Record<string, string> = {}) {
  snapshots.mockResolvedValue(list)
  const mounted = await mountWith(SnapshotPicker, {}, query)
  wrapper = mounted.wrapper
  return mounted
}

async function open() {
  await wrapper!.get('[data-testid="snapshot-picker"]').trigger('click')
  await flushPromises()
}

function option(date: string): HTMLElement {
  const element = document.body.querySelector<HTMLElement>(`[data-testid="snapshot-option-${date}"]`)
  if (!element) throw new Error(`No option for ${date}`)
  return element
}

/** Resolves once the router finishes its next navigation. */
function nextNavigation(router: Router): Promise<void> {
  return new Promise((resolve) => {
    const stop = router.afterEach(() => {
      stop()
      resolve()
    })
  })
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  snapshots.mockReset()
  // Menus render into an overlay container on the body; start each test without the last one's.
  document.body.innerHTML = ''
})

describe('SnapshotPicker', () => {
  it('shows the newest snapshot by default and lists every one, newest first', async () => {
    await mountPicker([SNAPSHOTS[2]!, SNAPSHOTS[0]!, SNAPSHOTS[1]!])

    expect(wrapper!.get('[data-testid="snapshot-picker"]').text()).toContain('Sep 28, 2026')
    await open()
    const options = [...document.body.querySelectorAll('[data-testid^="snapshot-option-"]')].map((element) => element.textContent?.trim())
    expect(options).toEqual(['Sep 28, 2026Newest', 'Aug 31, 2026', 'Jul 27, 2026'])
  })

  it('puts the picked snapshot in the URL, starting at page 1 and comparing with the file before it again', async () => {
    const { router } = await mountPicker(SNAPSHOTS, { code: '45378', page: '3', from: '3' })

    await open()
    const navigated = nextNavigation(router)
    option('2026-08-31').click()
    await navigated

    expect(router.currentRoute.value.query).toEqual({ code: '45378', snapshot: '2' })
  })

  it('leaves the newest snapshot out of the URL, so links follow the newest file', async () => {
    const { router } = await mountPicker(SNAPSHOTS, { code: '45378', snapshot: '2' })

    expect(wrapper!.get('[data-testid="snapshot-picker"]').text()).toContain('Aug 31, 2026')
    await open()
    const navigated = nextNavigation(router)
    option('2026-09-28').click()
    await navigated

    expect(router.currentRoute.value.query).toEqual({ code: '45378' })
  })

  it('says which files look incomplete', async () => {
    const partial = snapshot(3, '2026-07-27', {
      products: [product({ productLabel: 'Essential Plan', completeness: { score: 0.14, rates: 0.14, eins: 0.2, codes: 0.5, comparedWith: [1, 2], partial: true } })],
    })
    await mountPicker([SNAPSHOTS[0]!, SNAPSHOTS[1]!, partial])

    await open()

    expect(option('2026-07-27').textContent).toContain('Essential Plan looks incomplete')
  })

  it('stays out of the way with a single snapshot', async () => {
    await mountPicker([SNAPSHOTS[0]!])

    expect(wrapper!.find('[data-testid="snapshot-picker"]').exists()).toBe(false)
  })
})
