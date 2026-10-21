import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { QueryClient, VueQueryPlugin, notifyManager } from '@tanstack/vue-query'
import type { RateNpisPage, RateRow } from '../../api/types'
import NpiDialog from '../NpiDialog.vue'

const npis = vi.hoisted(() => vi.fn<(rateId: number, also: readonly number[], provider: string | null, page: number) => Promise<RateNpisPage>>())
vi.mock('../../api/ratesApi', () => ({ ratesApi: { npis } }))

// Query results reach the component as microtasks, so flushPromises settles them (no timers).
notifyManager.setScheduler(queueMicrotask)

function makeRow(multipleRates: boolean): RateRow {
  return {
    groupKey: 'g1',
    providerKey: '113466729',
    ein: '113466729',
    einDisplay: '11-3466729',
    einMasked: false,
    einLevel: false,
    businessName: 'ROCHESTER REGIONAL HEALTH, P.C.',
    otherNames: [],
    rate: 84,
    rateLow: 84,
    rateHigh: multipleRates ? 96 : 84,
    multipleRates,
    vsMedian: null,
    percentile: null,
    band: null,
    npiCount: 2,
    occurrences: 1,
    rateCount: multipleRates ? 2 : 1,
    firstRateId: 7,
    matched: true,
    rateIds: multipleRates ? [9, 7] : [7],
    wholeTaxIdRateId: null,
    profile: null,
    flags: [],
  }
}

const PAGE: RateNpisPage = {
  rateId: 7,
  providerKey: '113466729',
  total: 2,
  page: 1,
  pageSize: 50,
  items: [
    { npi: 1234567893, valid: true, rate: 84, rateHigh: 84 },
    { npi: 1234567890, valid: false, rate: 84, rateHigh: 96 },
  ],
}

let wrapper: VueWrapper | undefined

async function mountDialog(row: RateRow) {
  npis.mockResolvedValue(PAGE)
  wrapper = mount(NpiDialog, {
    props: { modelValue: true, row },
    attachTo: document.body,
    global: {
      plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), [VueQueryPlugin, { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) }]],
    },
  })
  await flushPromises()
  // The dialog renders in an overlay outside the wrapper.
  return document.querySelector('[data-testid="npi-dialog"]')!
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  npis.mockReset()
})

describe('NpiDialog', () => {
  it('titles the list with the plain provider name and lists every NPI as published', async () => {
    const dialog = await mountDialog(makeRow(false))
    expect(npis).toHaveBeenLastCalledWith(7, [], '113466729', 1, 50, expect.anything())
    const text = dialog.textContent ?? ''
    expect(text).toContain('Rochester Regional Health')
    expect(text).toContain('Tax ID 11-3466729')
    expect(dialog.querySelector('[data-testid="npi-count"]')!.textContent).toBe('2 clinicians (NPIs)')
    expect(text).toContain('1234567893')
    expect(text).toContain('Not a valid NPI')
    // One rate behind the row: no price per NPI.
    expect(text).not.toContain('$')
  })

  it("asks for every rate behind the row, the row's own first, and shows each NPI's price", async () => {
    const dialog = await mountDialog(makeRow(true))
    expect(npis).toHaveBeenLastCalledWith(7, [9], '113466729', 1, 50, expect.anything())
    expect(dialog.textContent).toContain('$84 – $96')
  })
})
