import { afterEach, describe, expect, it } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import type { HistorySnapshot, RateChange } from '../../api/types'
import { change } from '../../lib/__tests__/fixtures'
import ChangesTable from '../ChangesTable.vue'
import { mountWith } from './mountWith'

const august: HistorySnapshot = { snapshotId: 2, indexDate: '2026-08-31', isLatest: false, statisticsBuiltAt: null, statisticsCurrent: true }
const september: HistorySnapshot = { snapshotId: 1, indexDate: '2026-09-28', isLatest: true, statisticsBuiltAt: null, statisticsCurrent: true }

let wrapper: VueWrapper | undefined

async function mountList(items: RateChange[], total = items.length) {
  const mounted = await mountWith(ChangesTable, { rows: { total, page: 1, pageSize: 10, items }, from: august, to: september })
  wrapper = mounted.wrapper
  return mounted
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('ChangesTable', () => {
  it('lists each change as name, service and old to new price, with no codes or tax IDs', async () => {
    await mountList([
      change({ businessName: 'NHPE, LLC dba New Hyde Park Endoscopy' }),
      change({ providerKey: 'u1', einDisplay: '••-•••4567', einMasked: true, change: -50, changePct: -10, from: 500, to: 450 }),
      change({ providerKey: 'p3', feeType: 'professional', settingKey: 'office', code: '99213', codeName: 'Office E&M, established', from: 100.21, to: 100.4 }),
    ])

    const rows = wrapper!.findAll('[data-testid="change-row"]')
    expect(rows[0]!.text()).toContain('New Hyde Park Endoscopy')
    expect(rows[0]!.get('[data-testid="change-service"]').text()).toBe('Colonoscopy')
    expect(rows[0]!.get('[data-testid="change-text"]').text()).toBe('$600 → $660')
    expect(rows[0]!.get('[data-testid="change-text"]').classes()).toContain('text-high')
    expect(rows[1]!.get('[data-testid="change-text"]').classes()).toContain('text-low')
    // Whole dollars would read "$100 → $100".
    expect(rows[2]!.get('[data-testid="change-text"]').text()).toBe('$100.21 → $100.40')
    expect(rows[2]!.get('[data-testid="change-service"]').text()).toBe('Office visit, level 3')
    expect(wrapper!.text()).not.toMatch(/CPT|45378|EIN|tax ID|22-1111111|modifier/)
  })

  it('tells apart rows with the same provider and service by their bill or plan, and no more', async () => {
    await mountList([
      change({ providerKey: 'a', groupKey: 'ambetter|CPT|45378|facility|asc||negotiated' }),
      change({ providerKey: 'a', groupKey: 'ambetter|CPT|45378|professional|asc||negotiated', feeType: 'professional' }),
      change({ providerKey: 'b', groupKey: 'essential-plan|CPT|90834|facility|fqhc||negotiated', code: '90834', settingKey: 'fqhc', productLabel: 'Essential Plan', businessName: 'Community Health Project, Inc.' }),
      change({ providerKey: 'b', groupKey: 'ambetter|CPT|90834|facility|fqhc||negotiated', code: '90834', settingKey: 'fqhc', businessName: 'Community Health Project, Inc.' }),
      change({ providerKey: 'c', code: '99213', businessName: 'Other Clinic' }),
    ])

    const services = wrapper!.findAll('[data-testid="change-service"]').map((service) => service.text())
    expect(services).toEqual([
      'Colonoscopy · Surgery center',
      'Colonoscopy · Doctor · at a surgery center',
      'Therapy session, 45 minutes · Essential Plan',
      'Therapy session, 45 minutes · Ambetter',
      'Office visit, level 3',
    ])
  })

  it('opens a row to show its bill, plan, code, tax ID, several rates, and where else it is listed', async () => {
    await mountList([
      change({ kind: 'dropped', to: null, change: null, changePct: null, listedElsewhere: true }),
      change({ providerKey: 'p2', fromRates: 2, toRates: 3 }),
    ])

    const rows = wrapper!.findAll('[data-testid="change-row"]')
    expect(rows[0]!.get('[data-testid="change-text"]').text()).toBe('Gone · $600')
    expect(wrapper!.find('[data-testid="change-details"]').exists()).toBe(false)

    await rows[0]!.get('button').trigger('click')
    const details = rows[0]!.get('[data-testid="change-details"]')
    expect(details.get('[data-testid="change-bill"]').text()).toBe('Surgery center')
    expect(details.text()).toContain('Ambetter from Fidelis Care · Facility fee · Ambulatory surgery center')
    expect(details.text()).toContain('CPT 45378 · Colonoscopy')
    expect(details.text()).toContain('tax ID 22-1111111')
    expect(details.get('[data-testid="listed-elsewhere"]').text()).toBe('Also in the Sep 28, 2026 file, with other modifiers or setting.')
    expect(rows[0]!.get('button').attributes('aria-expanded')).toBe('true')

    await rows[1]!.get('button').trigger('click')
    expect(rows[1]!.get('[data-testid="several-rates"]').text()).toContain('Several rates: 2 then 3.')
    expect(rows[0]!.find('[data-testid="change-details"]').exists()).toBe(false)
  })

  it("links an opened row to the provider's rows in search, in the file that has them", async () => {
    await mountList([change(), change({ providerKey: 'other', kind: 'dropped', to: null, change: null, changePct: null })])
    const rows = wrapper!.findAll('[data-testid="change-row"]')

    await rows[0]!.get('button').trigger('click')
    expect(rows[0]!.get('[data-testid="change-search"]').attributes('href')).toBe(
      '/?code=45378&codeType=CPT&ein=221111111&group=ambetter|CPT|45378|facility|asc||negotiated',
    )
    await rows[1]!.get('button').trigger('click')
    expect(rows[1]!.get('[data-testid="change-search"]').attributes('href')).toBe(
      '/?code=45378&codeType=CPT&ein=other&group=ambetter|CPT|45378|facility|asc||negotiated&snapshot=2',
    )
  })

  it('pages through the rest, and says when nothing matches', async () => {
    await mountList([change()], 35)
    expect(wrapper!.find('[data-testid="changes-pages"]').exists()).toBe(true)
    await wrapper!.get('[data-testid="changes-pages"]').findAll('button').find((button) => button.text() === '2')!.trigger('click')
    expect(wrapper!.emitted('page')).toEqual([[2]])
    wrapper!.unmount()

    await mountList([])
    expect(wrapper!.get('[data-testid="changes-none"]').text()).toBe('No changes match.')
    expect(wrapper!.find('[data-testid="changes-pages"]').exists()).toBe(false)
  })
})
