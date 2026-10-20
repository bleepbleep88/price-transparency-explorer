import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, type VueWrapper } from '@vue/test-utils'
import type { HistorySnapshot, RateChange } from '../../api/types'
import { change } from '../../lib/__tests__/fixtures'
import ChangesTable from '../ChangesTable.vue'
import { mountWith } from './mountWith'

const august: HistorySnapshot = { snapshotId: 2, indexDate: '2026-08-31', isLatest: false, statisticsBuiltAt: null, statisticsCurrent: true }
const september: HistorySnapshot = { snapshotId: 1, indexDate: '2026-09-28', isLatest: true, statisticsBuiltAt: null, statisticsCurrent: true }

let wrapper: VueWrapper | undefined

async function mountTable(items: RateChange[], sort = 'largest') {
  const mounted = await mountWith(ChangesTable, { rows: { total: items.length, page: 1, pageSize: 25, items }, sort, from: august, to: september })
  wrapper = mounted.wrapper
  return mounted
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('ChangesTable', () => {
  it('shows each change with both files, in dollars and percent, under the files dates', async () => {
    await mountTable([change(), change({ providerKey: 'u1a2b3c4d5e6f', ein: null, einDisplay: '••-•••4567', einMasked: true, change: -50, changePct: -10, from: 500, to: 450 })])

    const headers = wrapper!.findAll('th').map((th) => th.text())
    expect(headers).toEqual(expect.arrayContaining(['Aug 31, 2026', 'Sep 28, 2026', 'Change']))
    const rows = wrapper!.findAll('tbody tr')
    expect(rows[0]!.text()).toContain('CPT 45378')
    expect(rows[0]!.text()).toContain('DIAGNOSTIC COLONOSCOPY')
    expect(rows[0]!.text()).toContain('Facility fee · Ambulatory surgery center')
    expect(rows[0]!.text()).toContain('EIN 22-1111111')
    expect(rows[0]!.get('[data-testid="change-text"]').text()).toBe('+$60.00 (+10.0%)')
    expect(rows[0]!.get('[data-testid="change-text"]').classes()).toContain('text-high')
    expect(rows[1]!.get('[data-testid="change-text"]').classes()).toContain('text-low')
    expect(rows[1]!.text()).toContain('Unverified tax ID')
  })

  it('marks new and dropped rates the other file only lists differently, and rates with several values', async () => {
    await mountTable([
      change({ kind: 'dropped', to: null, change: null, changePct: null, listedElsewhere: true }),
      change({ fromRates: 2, toRates: 3 }),
    ])

    const rows = wrapper!.findAll('tbody tr')
    expect(rows[0]!.get('[data-testid="change-text"]').text()).toBe('Dropped')
    expect(rows[0]!.find('[data-testid="listed-elsewhere"]').exists()).toBe(true)
    expect(rows[1]!.text()).toContain('of 2 rates')
    expect(rows[1]!.text()).toContain('of 3 rates')
    expect(rows[1]!.text()).toContain('Several rates')
  })

  it("links each row to the provider's rows in search, in the file that has them", async () => {
    await mountTable([change(), change({ kind: 'dropped', to: null, change: null, changePct: null })])

    const links = wrapper!.findAll('[data-testid="change-search"]').map((link) => link.attributes('href'))
    expect(links[0]).toBe('/?code=45378&codeType=CPT&ein=221111111&group=ambetter|CPT|45378|facility|asc||negotiated')
    expect(links[1]).toBe('/?code=45378&codeType=CPT&ein=221111111&group=ambetter|CPT|45378|facility|asc||negotiated&snapshot=2')
  })

  it('sorts by the change when its header is clicked, and back to the biggest moves', async () => {
    await mountTable([change()])

    await wrapper!.findAll('th').find((th) => th.text() === 'Change')!.find('.v-data-table-header__content').trigger('click')
    await flushPromises()

    expect(wrapper!.emitted('updateOptions')?.[0]).toEqual([{ page: 1, pageSize: 25, sort: 'changePct' }])
  })
})
