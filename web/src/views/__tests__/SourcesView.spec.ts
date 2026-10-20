import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { shallowRef } from 'vue'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import { QueryClient, VueQueryPlugin, notifyManager } from '@tanstack/vue-query'
import { ApiError } from '@/lib/apiClient'
import type { IngestionStatus } from '@/features/rates/api/types'
import type { SourceSnapshot } from '@/features/sources/api/types'
import { ALLOWED_FILE, sourceFile, sourceSnapshot } from '@/features/sources/lib/__tests__/fixtures'
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

describe('SourcesView', () => {
  it('shows each snapshot with its files, what was loaded, how it was cleaned and links to the files', async () => {
    const wrapper = await mountPage(Promise.resolve([sourceSnapshot()]))

    expect(wrapper.get('h1').text()).toBe('Sources and data quality')
    expect(wrapper.get('[data-testid="cleaning-rules"]').text()).toContain('Exact copies are removed')
    expect(wrapper.text()).not.toMatch(/median|percentile|quartile/i)
    const snapshot = wrapper.get('[data-testid="source-snapshot"]')
    expect(snapshot.get('h2').text()).toBe('Snapshot Aug 31, 2026')
    expect(snapshot.text()).toContain('Shown by default')
    expect(snapshot.text()).toContain('SHA-256 bb3a34c9f4194bc5e4e04e612b71979aa939eb3c15ca49e8f22774d8b9967f84')

    const files = wrapper.findAll('[data-testid="source-file"]')
    expect(files).toHaveLength(2)
    expect(files[0]!.text()).toContain('In-network rates · 85 MB')
    expect(files[0]!.text()).toContain('179,948 prices in the file became 7,727 unique rates after removing exact copies.')
    expect(files[0]!.text()).toContain('326 tax IDs use a prefix the IRS never issues, so they are masked.')
    expect(files[0]!.text()).toContain('Nothing was set aside.')
    expect(files[0]!.text()).toContain('Sep 2, 2026')
    expect(files[0]!.text()).toContain('"0x8DF0AB1A6895333"')
    expect(files[1]!.text()).toContain('Out-of-network allowed amounts · 508 KB')
    expect(files[1]!.text()).toContain('1,033 amounts in the file became 605 different amounts after removing exact copies.')
    expect(files[1]!.text()).toContain('6 EINs published as 8 digits, given back their leading zero.')

    const links = files.map((file) => file.findAll('a').find((a) => a.text().includes('Open the file'))?.attributes('href'))
    expect(links).toEqual([sourceFile().url, ALLOWED_FILE.url])
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
    const file = wrapper.get('[data-testid="source-file"]')
    expect(file.get('[data-testid="source-file-status"]').text()).toBe('pending')
    expect(file.text()).toContain('Not loaded yet.')
    expect(file.text()).not.toContain('How it was cleaned')
  })

  it('says so when nothing could be read, and offers a retry when the request fails', async () => {
    const empty = await mountPage(Promise.resolve([]))
    expect(empty.text()).toContain('Nothing loaded yet')

    const failed = await mountPage(Promise.reject(new ApiError(500, 'Something went wrong on the server.')))
    expect(failed.text()).toContain('Something went wrong on the server.')
    expect(failed.text()).toContain('Try again')
  })
})
