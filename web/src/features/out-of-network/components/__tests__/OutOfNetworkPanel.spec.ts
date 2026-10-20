import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import { parseRouteQuery } from '@/features/rates/lib/searchParams'
import type { OutOfNetworkGroup } from '../../api/types'
import { LISTED_CT_READ, oonGroup, oonResult } from '../../lib/__tests__/fixtures'
import OutOfNetworkPanel from '../OutOfNetworkPanel.vue'

// Not said anywhere in the panel (decision 89): statistics words, a "typical" amount, or "payments" as a count.
const NOT_SAID = /median|percentile|quartile|÷|×|typical|payment/i

async function mountPanel(groups: OutOfNetworkGroup[], query: Record<string, string> = {}, statuses = { ambetter: 'loaded', essential: 'loaded' }) {
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
      { fileId: 3, productKey: 'ambetter-from-fidelis-care', productLabel: 'Ambetter from Fidelis Care', url: 'https://example.test/ex_allowed-amounts.json', status: statuses.ambetter, fileLastUpdatedOn: '2026-08-31' },
      { fileId: 4, productKey: 'essential-plan', productLabel: 'Essential Plan', url: 'https://example.test/es_allowed-amounts.json', status: statuses.essential, fileLastUpdatedOn: '2026-08-31' },
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

  it('leads each row with how billed compares with allowed, then the amounts listed, never a typical amount', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ])

    expect(wrapper.text()).toContain("What providers outside the plan's network billed for 0450, and what the plan allowed.")
    expect(wrapper.findAll('th').map((th) => th.text())).toEqual([
      'Product and fee',
      'Billed vs allowed',
      'Allowed amounts listed',
      'Billed charges listed',
      'Listed for',
    ])
    const rows = wrapper.findAll('[data-testid="oon-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Essential Plan · Facility fee')
    expect(rows[0]!.text()).toContain('Emergency room (hospital)')
    expect(rows[0]!.text()).toContain('20 different amounts listed')
    expect(rows[0]!.text()).toContain('5 providers')
    expect(rows[0]!.get('[data-testid="oon-share"]').text()).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(rows[0]!.get('[data-testid="oon-allowed"]').text()).toBe('Ran from $60.12 to $187,539')
    expect(rows[0]!.get('[data-testid="oon-billed"]').text()).toBe('Ran from $552 to $1,733,257')

    // Under 5 providers: every amount, and nothing compared.
    expect(rows[1]!.text()).toContain("Doctor's fee")
    expect(rows[1]!.get('[data-testid="oon-allowed"]').text()).toBe('$218, $545, $763 and $8,374')
    expect(rows[1]!.get('[data-testid="oon-share"]').text()).toBe('Only 1 provider: too few to compare.')
    expect(wrapper.text()).not.toMatch(NOT_SAID)
  })

  it('says how the files work and only what they support about the amounts, with the per-unit note only for per-unit codes', async () => {
    const facility = await mountPanel([oonGroup()])
    const notes = facility.get('[data-testid="oon-notes"]').text()
    expect(notes).toContain('20 or more claims in a 90-day window')
    expect(notes).toContain("The files don't say whether an amount covers one visit or several, so compare these with in-network prices carefully.")
    expect(notes).not.toMatch(/look like|totals/i)
    expect(notes).not.toContain('per unit')

    const anesthesia = await mountPanel([oonGroup({ codeType: 'CPT', code: '00812', feeType: 'professional', perUnitCode: true })])
    expect(anesthesia.get('[data-testid="oon-notes"]').text()).toContain(
      "In-network rates for this code are per unit; the files don't say how many units an out-of-network amount covers.",
    )
  })

  it('says when a file was only partly read or could not be loaded', async () => {
    const wrapper = await mountPanel([oonGroup()], {}, { ambetter: 'failed', essential: 'partial' })
    expect(wrapper.findAll('[data-testid="oon-file-note"]').map((note) => note.text())).toEqual([
      "Ambetter from Fidelis Care's out-of-network file couldn't be loaded, so its amounts aren't shown.",
      "Essential Plan's out-of-network file was only partly read, so some amounts may be missing.",
    ])
    expect((await mountPanel([oonGroup()])).find('[data-testid="oon-file-note"]').exists()).toBe(false)
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
    expect(rows[0]!.get('[data-testid="oon-share"]').text()).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(rows[0]!.get('[data-testid="oon-allowed"]').text()).toBe('Allowed amounts listed ran from $60.12 to $187,539.')
    expect(rows[1]!.get('[data-testid="oon-allowed"]').text()).toBe('Allowed amounts listed: $218, $545, $763 and $8,374.')
    expect(rows[1]!.get('[data-testid="oon-share"]').text()).toBe('Only 1 provider: too few to compare.')
    expect(rows[1]!.text()).toContain('4 different amounts listed for 1 provider')
    expect(wrapper.text()).not.toMatch(NOT_SAID)
  })

  it('a billed charge nobody published reads as such', async () => {
    const wrapper = await mountPanel([oonGroup({ billed: null, billedToAllowed: null })])

    expect(wrapper.get('[data-testid="oon-billed"]').text()).toBe('Not published')
    expect(wrapper.get('[data-testid="oon-share"]').text()).toBe('Too few billed charges published to compare.')
  })
})
