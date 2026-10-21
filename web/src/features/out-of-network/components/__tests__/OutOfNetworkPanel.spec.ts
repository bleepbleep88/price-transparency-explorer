import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import { parseRouteQuery } from '@/features/rates/lib/searchParams'
import type { OutOfNetworkGroup } from '../../api/types'
import { LISTED_CT_READ, oonGroup, oonResult } from '../../lib/__tests__/fixtures'
import OutOfNetworkPanel from '../OutOfNetworkPanel.vue'

// Not said anywhere in the panel (decision 89): statistics words, a "typical" amount, or "payments" as a count.
const NOT_SAID = /median|percentile|quartile|÷|×|typical|payment/i
// Not on the main view (decision 90): codes, code types, modifiers, tax IDs or NPIs.
const NOT_ON_MAIN_VIEW = /0450|74177|\bRC\b|CPT|HCPCS|modifier|tax ID|\bEINs?\b|\bNPIs?\b/i

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

/** The panel's text with Details left closed (v-show keeps its contents in the DOM). */
function mainViewText(wrapper: Awaited<ReturnType<typeof mountPanel>>): string {
  const element = wrapper.element.cloneNode(true) as HTMLElement
  element.querySelectorAll('.details-body').forEach((body) => body.remove())
  return element.textContent ?? ''
}

describe('OutOfNetworkPanel', () => {
  it('says one thing up front, for the bill with the most providers, and keeps the rest in Details', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ])

    expect(wrapper.get('h2').text()).toBe('Out of network')
    expect(wrapper.get('[data-testid="oon-line"]').text()).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(wrapper.get('[data-testid="oon-which"]').text()).toBe('Essential Plan · Facility fee')
    expect(wrapper.get('[data-testid="oon-details"] .details-body').attributes('style')).toContain('display: none')
    const main = mainViewText(wrapper)
    expect(main).not.toMatch(NOT_ON_MAIN_VIEW)
    expect(main).not.toMatch(NOT_SAID)
  })

  it('shows every bill in Details: how billed compares with allowed, then the amounts listed, never a typical amount', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ])
    await wrapper.get('[data-testid="oon-details-toggle"]').trigger('click')

    const rows = wrapper.findAll('[data-testid="oon-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Essential Plan · Facility fee')
    expect(rows[0]!.text()).toContain('Emergency room (hospital)')
    expect(rows[0]!.text()).toContain('20 different amounts listed for 5 providers')
    expect(rows[0]!.get('[data-testid="oon-share"]').text()).toBe('Providers usually billed about 4.8 times what the plan allowed.')
    expect(rows[0]!.get('[data-testid="oon-allowed"]').text()).toBe('Allowed amounts listed ran from $60.12 to $187,539.')
    expect(rows[0]!.get('[data-testid="oon-billed"]').text()).toBe('Billed charges listed ran from $552 to $1,733,257.')

    // Under 5 providers: every amount, and nothing compared.
    expect(rows[1]!.text()).toContain("Doctor's fee")
    expect(rows[1]!.get('[data-testid="oon-allowed"]').text()).toBe('Allowed amounts listed: $218, $545, $763 and $8,374.')
    expect(rows[1]!.get('[data-testid="oon-share"]').text()).toBe('Only 1 provider: too few to compare.')
    expect(rows[1]!.text()).toContain('4 different amounts listed for 1 provider')
    expect(wrapper.text()).not.toMatch(NOT_SAID)
  })

  it('under 5 providers, the one line says how few and lists what the plan allowed', async () => {
    const wrapper = await mountPanel([LISTED_CT_READ])
    expect(wrapper.get('[data-testid="oon-line"]').text()).toBe('Only 1 place: the plan allowed $218 · $545 · $763 · $8,374.')
    expect(wrapper.find('[data-testid="oon-which"]').exists()).toBe(false)
  })

  it('says how the files work and only what they support, with the per-unit note only for per-unit codes', async () => {
    const facility = await mountPanel([oonGroup()])
    const notes = facility.get('[data-testid="oon-notes"]').text()
    expect(notes).toContain('20 or more claims in a 90-day window')
    expect(notes).toContain("The files don't say whether an amount covers one visit or several.")
    expect(notes).not.toMatch(/look like|totals/i)
    expect(notes).not.toContain('per unit')

    const anesthesia = await mountPanel([oonGroup({ codeType: 'CPT', code: '00812', feeType: 'professional', perUnitCode: true })])
    expect(anesthesia.get('[data-testid="oon-notes"]').text()).toContain(
      "In-network prices for this code are per unit; these files don't say how many units an amount covers.",
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

  it('follows the search filters and links to the sources page and the files behind its bills', async () => {
    const wrapper = await mountPanel([oonGroup(), LISTED_CT_READ], { product: 'essential-plan' })

    expect(wrapper.findAll('[data-testid="oon-row"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="oon-which"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="oon-sources-link"]').attributes('href')).toBe('/sources')
    const fileLinks = wrapper.findAll('a').filter((a) => a.text().endsWith(' file'))
    expect(fileLinks.map((a) => [a.text(), a.attributes('href')])).toEqual([['Essential Plan file', 'https://example.test/es_allowed-amounts.json']])
  })

  it('a billed charge nobody published reads as such', async () => {
    const wrapper = await mountPanel([oonGroup({ billed: null, billedToAllowed: null })])

    expect(wrapper.get('[data-testid="oon-line"]').text()).toBe('The plan allowed $60.12\u00a0–\u00a0$187,539.')
    expect(wrapper.get('[data-testid="oon-billed"]').text()).toBe('Billed charges not published.')
    expect(wrapper.get('[data-testid="oon-share"]').text()).toBe('Too few billed charges published to compare.')
  })
})
