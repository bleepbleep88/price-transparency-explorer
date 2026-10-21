import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent, h, type PropType } from 'vue'
import { createVuetify } from 'vuetify'
import { VLayout } from 'vuetify/components'
import { QueryClient, VueQueryPlugin, notifyManager } from '@tanstack/vue-query'
import type { RateSource, RateSourceProviderGroup } from '../../api/types'
import SourceDrawer from '../SourceDrawer.vue'

const source = vi.hoisted(() => vi.fn<(rateId: number, provider: string | null, signal?: AbortSignal) => Promise<RateSource>>())
vi.mock('../../api/ratesApi', () => ({ ratesApi: { source } }))

// Query results reach the component as microtasks, so flushPromises settles them (no timers).
notifyManager.setScheduler(queueMicrotask)

const ROW_RATE = 4294973218
const WHOLE_TAX_ID_RATE = 4294973204
const PROVIDER = '161078102'

// Shaped like the two 90834 rates behind one row at EIN 16-1078102: the row's own rate lists one NPI, the other is a
// contract with the whole tax ID (npi [0] in the file).
function providerGroup(wholeTaxId: boolean): RateSourceProviderGroup {
  return {
    providerGroupId: wholeTaxId ? 845 : 914,
    groupIndex: wholeTaxId ? 232 : 209,
    taxIdDisplay: '16-1078102',
    taxIdMasked: false,
    businessName: 'Alcohol & Dependency Services, Inc/Buffalo',
    npiCount: wholeTaxId ? 0 : 1,
    wholeTaxId,
    networkNames: null,
    providerMatch: true,
  }
}

function makeSource(rateId: number): RateSource {
  const wholeTaxId = rateId === WHOLE_TAX_ID_RATE
  return {
    rateId,
    file: {
      fileId: 1,
      productKey: 'ambetter-from-fidelis-care',
      productLabel: 'Ambetter from Fidelis Care',
      url: 'https://www.centene.com/in-network.json',
      sha256: null,
      lastUpdatedOn: '2026-09-02',
      httpLastModified: '2026-09-04T18:23:42+00:00',
      contentLength: 89359830,
      reportingEntityName: 'Centene Management Company LLC',
      schemaVersion: '2.0.0',
    },
    snapshot: { snapshotId: 1, indexUrl: 'https://www.centene.com/index.json', indexDate: '2026-08-31', indexLastUpdatedOn: '2026-08-31' },
    jsonPath: `$.in_network[1236].negotiated_rates[${wholeTaxId ? 0 : 14}].negotiated_prices[0]`,
    billingCodeType: 'CPT',
    billingCode: '90834',
    billingCodePublished: '90834',
    codeType: 'CPT',
    code: '90834',
    codeSource: 'billing_code',
    additionalInformation: null,
    negotiationArrangement: 'ffs',
    negotiatedType: 'negotiated',
    rate: 84,
    expirationDate: null,
    billingClass: 'professional',
    setting: 'outpatient',
    places: [{ pos: '02', name: "Telehealth provided other than in patient's home" }],
    modifiers: [{ code: '95', label: 'Telehealth (audio and video)' }],
    occurrences: 1,
    itemLabelCount: 1,
    itemLabelSamples: ['PSYTX W PT 45 MINUTES'],
    providerGroups: [providerGroup(wholeTaxId)],
    providerGroupCount: wholeTaxId ? 333 : 571,
  }
}

// The drawer needs a Vuetify layout around it; `open` stands in for the page's v-model.
const Host = defineComponent({
  props: {
    open: { type: Boolean, required: true },
    wholeTaxIdRateId: { type: Number as PropType<number | null>, default: null },
  },
  setup(props) {
    return () =>
      h(VLayout, () =>
        h(SourceDrawer, {
          modelValue: props.open,
          rateId: ROW_RATE,
          providerName: 'Alcohol & Dependency Services, Inc/Buffalo',
          providerKey: PROVIDER,
          wholeTaxIdRateId: props.wholeTaxIdRateId,
        }),
      )
  },
})

let wrapper: VueWrapper | undefined

async function mountDrawer(wholeTaxIdRateId: number | null) {
  source.mockImplementation((rateId) => Promise.resolve(makeSource(rateId)))
  wrapper = mount(Host, {
    props: { open: true, wholeTaxIdRateId },
    attachTo: document.body,
    global: {
      plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), [VueQueryPlugin, { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) }]],
    },
  })
  await flushPromises()
  return wrapper
}

const buttonNamed = (w: VueWrapper, name: string) => w.findAll('button').find((b) => b.text() === name)

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  source.mockReset()
})

describe('SourceDrawer', () => {
  it("opens the row's whole-tax-ID contract for the same provider, and goes back", async () => {
    const w = await mountDrawer(WHOLE_TAX_ID_RATE)
    expect(source).toHaveBeenLastCalledWith(ROW_RATE, PROVIDER, expect.anything())
    expect(w.get('[data-testid="whole-tax-id-contract"]').text()).toContain('Also a whole-tax-ID contract (no NPIs).')
    expect(w.find('[data-testid="whole-tax-id-shown"]').exists()).toBe(false)

    await buttonNamed(w, 'Show it')!.trigger('click')
    await flushPromises()
    expect(source).toHaveBeenLastCalledWith(WHOLE_TAX_ID_RATE, PROVIDER, expect.anything())
    expect(w.text()).toContain('whole tax ID, no NPIs')
    expect(w.get('[data-testid="source-json-path"]').text()).toBe('$.in_network[1236].negotiated_rates[0].negotiated_prices[0]')
    expect(w.find('[data-testid="whole-tax-id-contract"]').exists()).toBe(false)
    expect(w.get('[data-testid="whole-tax-id-shown"]').text()).toContain("Showing the whole-tax-ID contract.")
    // The pressed button is gone; focus moves to the drawer's heading rather than being lost.
    expect(document.activeElement?.textContent).toBe('Source of this price')

    await buttonNamed(w, 'Back to this price')!.trigger('click')
    await flushPromises()
    expect(w.get('[data-testid="source-json-path"]').text()).toBe('$.in_network[1236].negotiated_rates[14].negotiated_prices[0]')
    expect(w.find('[data-testid="whole-tax-id-contract"]').exists()).toBe(true)
  })

  it("starts on the row's own rate each time it opens", async () => {
    const w = await mountDrawer(WHOLE_TAX_ID_RATE)
    await buttonNamed(w, 'Show it')!.trigger('click')
    await flushPromises()
    expect(w.find('[data-testid="whole-tax-id-shown"]').exists()).toBe(true)

    await w.setProps({ open: false })
    await w.setProps({ open: true })
    await flushPromises()
    expect(w.get('[data-testid="source-json-path"]').text()).toBe('$.in_network[1236].negotiated_rates[14].negotiated_prices[0]')
    expect(w.find('[data-testid="whole-tax-id-shown"]').exists()).toBe(false)
    expect(w.find('[data-testid="whole-tax-id-contract"]').exists()).toBe(true)
  })

  it('says nothing about a whole-tax-ID contract when the row has none', async () => {
    const w = await mountDrawer(null)
    expect(w.find('[data-testid="source-json-path"]').exists()).toBe(true)
    expect(w.find('[data-testid="whole-tax-id-contract"]').exists()).toBe(false)
    expect(buttonNamed(w, 'Show it')).toBeUndefined()
  })
})
