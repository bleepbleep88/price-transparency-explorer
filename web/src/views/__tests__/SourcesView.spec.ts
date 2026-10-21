import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { shallowRef } from 'vue'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import { QueryClient, VueQueryPlugin, notifyManager } from '@tanstack/vue-query'
import { ApiError } from '@/lib/apiClient'
import type { IngestionStatus } from '@/features/rates/api/types'
import type { SourceSnapshot } from '@/features/sources/api/types'
import { ALLOWED_FILE, septemberSnapshot, sourceFile, sourceSnapshot } from '@/features/sources/lib/__tests__/fixtures'
import SourcesView from '../SourcesView.vue'

const list = vi.hoisted(() => vi.fn<(signal?: AbortSignal) => Promise<SourceSnapshot[]>>())
vi.mock('@/features/sources/api/sourcesApi', () => ({ sourcesApi: { list } }))

// The live status stream isn't under test here: loading is either finished or not.
const status = vi.hoisted(() => ({ current: null as IngestionStatus | null }))
vi.mock('@/features/status/useDataStatus', () => ({
  useDataStatus: () => ({ status: shallowRef(status.current), progress: shallowRef(null), connected: shallowRef(true), dataAvailable: shallowRef(true) }),
}))

// Query results reach the component as microtasks, so flushPromises settles them (no timers).
notifyManager.setScheduler(queueMicrotask)

// Not on the main view (decision 90): statistics words, code types, tax IDs, NPIs, hashes, "snapshot" or file names.
const NOT_ON_MAIN_VIEW = /median|percentile|quartile|snapshot|sha-256|etag|\bnpis?\b|\beins?\b|tax id|\.json|in-network rates|allowed amounts/i

function readyStatus(phase: IngestionStatus['phase'] = 'ready'): IngestionStatus {
  return {
    phase,
    message: phase === 'ready' ? 'Data is ready.' : 'Downloading Essential Plan rates…',
    dataAvailable: true,
    currentFile: null,
    currentFileProgress: null,
    filesDone: 1,
    filesTotal: 2,
    issues: [],
    updatedAt: '2026-10-20T09:40:07Z',
    version: 1,
  }
}

async function mountPage(answer: Promise<SourceSnapshot[]>, phase: IngestionStatus['phase'] = 'ready') {
  list.mockReset().mockReturnValue(answer)
  status.current = readyStatus(phase)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: { template: '<div />' } },
      { path: '/sources', name: 'sources', component: SourcesView },
    ],
  })
  await router.push('/sources')
  await router.isReady()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = mount(SourcesView, {
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router, [VueQueryPlugin, { queryClient }]] },
  })
  await flushPromises()
  return wrapper
}

/** The page's text with every Details panel left closed (v-show keeps their contents in the DOM). */
function mainViewText(wrapper: Awaited<ReturnType<typeof mountPage>>): string {
  const element = wrapper.element.cloneNode(true) as HTMLElement
  element.querySelectorAll('.details-body').forEach((body) => body.remove())
  return element.textContent ?? ''
}

describe('SourcesView', () => {
  it('leads with one total, then each month with its plans and links to their files', async () => {
    const older = { ...sourceSnapshot(), snapshotId: 1, isLatest: false }
    const wrapper = await mountPage(Promise.resolve([septemberSnapshot(), older]))

    expect(wrapper.get('h1').text()).toBe('Where the data comes from')
    expect(wrapper.text()).toContain('2 months of public files from Centene Management Company')
    const total = wrapper.get('[data-testid="sources-total"]')
    expect(total.get('.big').attributes('aria-label')).toBe('573,393 prices → 17,140 after removing copies')
    expect(total.get('.big').text().replace(/\s+/g, ' ')).toBe('573,393 prices → 17,140 after removing copies')
    expect(total.text()).toContain('Sep 28, 2026 files')

    const months = wrapper.findAll('[data-testid="source-snapshot"]')
    expect(months.map((month) => month.get('h2').text())).toEqual(['Sep 28, 2026', 'Aug 31, 2026'])
    expect(months[0]!.text()).toContain('Latest')
    expect(months[1]!.text()).not.toContain('Latest')
    const plans = months[0]!.findAll('[data-testid="source-plan"]')
    expect(plans.map((plan) => plan.get('.plan-name').text())).toEqual(['Ambetter from Fidelis Care', 'Essential Plan'])
    const prices = plans[1]!.get('[data-testid="source-link-prices"]')
    expect(prices.attributes('href')).toBe('https://www.centene.com/2026-09-28_centene-management-company-llc_fidelis-es_in-network.json')
    // Each link downloads the whole file, so it says how big it is.
    expect(prices.text()).toBe('Prices · 85 MB')
    expect(prices.attributes('aria-label')).toBe('Essential Plan prices file, Sep 28, 2026, 85 MB')
    const outOfNetwork = plans[1]!.get('[data-testid="source-link-oon"]')
    expect(outOfNetwork.attributes('href')).toBe(ALLOWED_FILE.url)
    expect(outOfNetwork.text()).toBe('Out of network · 508 KB')
    expect(wrapper.find('[data-testid="source-plan-problem"]').exists()).toBe(false)

    expect(mainViewText(wrapper)).not.toMatch(NOT_ON_MAIN_VIEW)
  })

  it("keeps what each file held, how it was cleaned and how to trace it in the month's Details", async () => {
    const wrapper = await mountPage(Promise.resolve([sourceSnapshot()]))
    const details = wrapper.get('[data-testid="source-snapshot"]').get('[data-testid="details"]')
    expect(details.get('.details-body').attributes('style')).toContain('display: none')
    await details.get('[data-testid="details-toggle"]').trigger('click')
    expect(details.get('.details-body').attributes('style') ?? '').not.toContain('display: none')

    expect(details.text()).toContain('Published by Centene Management Company LLC (Third Party Administrator).')
    expect(details.text()).toContain('SHA-256 bb3a34c9f4194bc5e4e04e612b71979aa939eb3c15ca49e8f22774d8b9967f84')
    const files = details.findAll('[data-testid="source-file"]')
    expect(files).toHaveLength(2)
    expect(files[0]!.get('h4').text()).toBe('Ambetter from Fidelis Care · In-network rates 85 MB')
    expect(files[0]!.text()).toContain('179,948 prices in the file became 7,727 unique rates after removing exact copies.')
    expect(files[0]!.text()).toContain('326 tax IDs use a prefix the IRS never issues, so they are masked.')
    expect(files[0]!.text()).toContain('Nothing was set aside.')
    expect(files[0]!.text()).toContain('List of files Aug 31, 2026 · file says Sep 2, 2026 · server Sep 4, 2026')
    expect(files[0]!.text()).toContain('"0x8DF0AB1A6895333"')
    expect(files[1]!.get('h4').text()).toBe('Essential Plan · Out-of-network allowed amounts 508 KB')
    expect(files[1]!.text()).toContain('1,033 amounts in the file became 605 different amounts after removing exact copies.')
    expect(files[1]!.text()).toContain('6 EINs published as 8 digits, given back their leading zero.')
    const links = files.map((file) => file.findAll('a').find((a) => a.text().includes('Open the file'))?.attributes('href'))
    expect(links).toEqual([sourceFile().url, ALLOWED_FILE.url])

    const rules = wrapper.get('[data-testid="cleaning-rules"]')
    expect(rules.get('[data-testid="cleaning-rules-toggle"]').text()).toBe('How files are cleaned')
    expect(rules.text()).toContain('Exact copies are counted once.')
  })

  it('lists what was set aside, why, how many and the first example', async () => {
    const rejected = sourceFile(
      {
        rejects: [
          { reason: 'allowed_amount_not_a_number', description: "An allowed amount that isn't a number. The payment is skipped.", count: 2, example: 'out_of_network[0].allowed_amounts[0].payments[0]' },
          { reason: 'ein_not_nine_digits', description: "A tax ID marked as an EIN that isn't 9 digits. Kept as published, not read as an EIN.", count: 1, example: '••-3456' },
        ],
      },
      { rejected: 2 },
    )
    const wrapper = await mountPage(Promise.resolve([sourceSnapshot([rejected])]))

    const rows = wrapper.findAll('[data-testid="source-reject"]').map((row) => row.findAll('td').map((cell) => cell.text()))
    expect(rows).toEqual([
      ["An allowed amount that isn't a number. The payment is skipped.", '2', 'out_of_network[0].allowed_amounts[0].payments[0]'],
      ["A tax ID marked as an EIN that isn't 9 digits. Kept as published, not read as an EIN.", '1', '••-3456'],
    ])
    expect(wrapper.find('[data-testid="source-no-rejects"]').exists()).toBe(false)
  })

  it('shows a file that is not loaded yet, and the loading panel while loading goes on', async () => {
    const pending = sourceFile({ status: 'pending', counts: null, sha256: null, eTag: null, timing: { downloadMs: null, parseMs: null, loadedAt: null } })
    const wrapper = await mountPage(Promise.resolve([sourceSnapshot([pending])]), 'downloading')

    expect(wrapper.find('[data-testid="loading-panel"]').exists()).toBe(true)
    // No total until a rate file is loaded.
    expect(wrapper.find('[data-testid="sources-total"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="source-plan-problem"]').text()).toBe('Prices file: not loaded yet')
    const file = wrapper.get('[data-testid="source-file"]')
    expect(file.text()).toContain('Not loaded yet.')
    expect(file.find('[data-testid="source-no-rejects"]').exists()).toBe(false)
  })

  it('says so when nothing could be read, and offers a retry when the request fails', async () => {
    const empty = await mountPage(Promise.resolve([]))
    expect(empty.text()).toContain('Nothing loaded yet.')
    expect(empty.find('[data-testid="cleaning-rules"]').exists()).toBe(false)

    const failed = await mountPage(Promise.reject(new ApiError(500, 'Something went wrong on the server.')))
    expect(failed.text()).toContain('Something went wrong on the server.')
    expect(failed.text()).toContain('Try again')
  })
})
