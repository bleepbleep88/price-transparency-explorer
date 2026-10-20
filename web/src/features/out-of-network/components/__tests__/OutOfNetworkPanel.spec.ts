import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import { parseRouteQuery } from '@/features/rates/lib/searchParams'
import type { OutOfNetworkGroup } from '../../api/types'
import { LISTED_CT_READ, oonGroup, oonResult } from '../../lib/__tests__/fixtures'
import OutOfNetworkPanel from '../OutOfNetworkPanel.vue'

async function mountPanel(groups: OutOfNetworkGroup[], query: Record<string, string> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: { template: '<div />' } },
      { path: '/sources', name: 'sources', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const result = {
    ...oonResult(groups),
    files: [
      { fileId: 3, productKey: 'ambetter-from-fidelis-care', productLabel: 'Ambetter from Fidelis Care', url: 'https://example.test/ex_allowed-amounts.json', status: 'loaded', fileLastUpdatedOn: '2026-08-31' },
      { fileId: 4, productKey: 'essential-plan', productLabel: 'Essential Plan', url: 'https://example.test/es_allowed-amounts.json', status: 'loaded', fileLastUpdatedOn: '2026-08-31' },
    ],
  }
  const wrapper = mount(OutOfNetworkPanel, {
    props: { result, params: parseRouteQuery({ code: '0450', ...query }) },
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router] },
  })
  await flushPromises()
  return wrapper
}

describe('OutOfNetworkPanel', () => {
  const desktopWidth = window.innerWidth
  afterEach(() => {
    window.innerWidth = desktopWidth
  })

  it('has a row per product and fee: payments, allowed, billed and the share allowed', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ])

    expect(wrapper.text()).toContain('What the plan allowed when it paid providers outside its network for 0450.')
    const rows = wrapper.findAll('[data-testid="oon-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Essential Plan · Facility fee')
    expect(rows[0]!.text()).toContain('Emergency room (hospital)')
    expect(rows[0]!.text()).toContain('20 payments')
    expect(rows[0]!.text()).toContain('5 providers')
    expect(rows[0]!.get('[data-testid="oon-allowed"]').text()).toBe('median $1,375.18 $60.12 to $187,538.55')
    expect(rows[0]!.get('[data-testid="oon-billed"]').text()).toBe('median $5,110.00 $551.92 to $1,733,257.00')
    expect(rows[0]!.get('[data-testid="oon-share"]').text()).toBe('median 21%')

    // Under 5 amounts: every amount, no midpoint, and no share.
    expect(rows[1]!.get('[data-testid="oon-allowed"]').text()).toBe('$217.92, $544.80, $762.72 and $8,373.66')
    expect(rows[1]!.get('[data-testid="oon-share"]').text()).toBe('too few to say')
  })

  it('says how the files work and what the amounts are, adding the per-unit note only for per-unit codes', async () => {
    const facility = await mountPanel([oonGroup()])
    const notes = facility.get('[data-testid="oon-notes"]').text()
    expect(notes).toContain('20 or more claims for the service in a 90-day window')
    expect(notes).toContain('many look like totals of several claims')
    expect(notes).not.toContain('per unit')

    const anesthesia = await mountPanel([oonGroup({ codeType: 'CPT', code: '00812', feeType: 'professional', perUnitCode: true })])
    expect(anesthesia.get('[data-testid="oon-notes"]').text()).toContain('Negotiated rates for this code are per unit')
  })

  it('follows the search filters and links to the sources page and the files behind its rows', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ], { product: 'essential-plan' })

    expect(wrapper.findAll('[data-testid="oon-row"]')).toHaveLength(1)
    expect(wrapper.get('[data-testid="oon-sources-link"]').attributes('href')).toBe('/sources')
    const fileLinks = wrapper.findAll('a').filter((a) => a.text().endsWith(' file'))
    expect(fileLinks.map((a) => [a.text(), a.attributes('href')])).toEqual([['Essential Plan file', 'https://example.test/es_allowed-amounts.json']])
  })

  it('stacks each group as lines on a phone, where five columns would not fit', async () => {
    window.innerWidth = 390
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ])

    expect(wrapper.find('table').exists()).toBe(false)
    const rows = wrapper.findAll('[data-testid="oon-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.get('[data-testid="oon-allowed"]').text()).toBe('Allowed median $1,375.18 ($60.12 to $187,538.55)')
    expect(rows[0]!.get('[data-testid="oon-share"]').text()).toBe('Allowed ÷ billed: median 21%')
    expect(rows[1]!.find('[data-testid="oon-share"]').exists()).toBe(false)
  })

  it('a billed charge nobody published reads as such', async () => {
    const wrapper = await mountPanel([oonGroup({ billed: null, allowedShareOfBilled: null })])

    expect(wrapper.get('[data-testid="oon-billed"]').text()).toBe('Not published')
  })
})
