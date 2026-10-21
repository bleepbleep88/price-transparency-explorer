import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { shallowRef } from 'vue'
import { createVuetify } from 'vuetify'
import type { ProductFileSummary, SnapshotSummary } from '../../api/types'
import DatasetSnapshot from '../DatasetSnapshot.vue'

// The plans list only loads once opened; not under test here.
vi.mock('../../composables/useRateQueries', () => ({ usePlans: () => ({ isPending: shallowRef(false), data: shallowRef(null) }) }))

function product(overrides: Partial<ProductFileSummary>): ProductFileSummary {
  return {
    fileId: 1,
    productKey: 'ambetter-from-fidelis-care',
    productLabel: 'Ambetter from Fidelis Care',
    url: 'https://example.test/ex_in-network.json',
    status: 'loaded',
    error: null,
    warning: null,
    fileLastUpdatedOn: '2026-10-06',
    httpLastModified: '2026-10-08T17:48:22+00:00',
    contentLength: 95466319,
    sha256: null,
    planEntries: 88,
    distinctPlanIds: 23,
    items: 1554,
    pricesPublished: 193435,
    uniqueRates: 7643,
    duplicatesRemoved: 185792,
    distinctNpis: 41544,
    distinctEins: 5890,
    loadedAt: '2026-10-20T19:22:55Z',
    completeness: null,
    ...overrides,
  }
}

// The real 2026-09-28 snapshot: 7,643 + 9,497 prices.
function snapshot(essential: Partial<ProductFileSummary> = {}): SnapshotSummary {
  return {
    snapshotId: 1,
    indexDate: '2026-09-28',
    indexUrl: 'https://example.test/2026-09-28_fidelis_index.json',
    indexLastUpdatedOn: '2026-09-28',
    reportingEntityName: 'Centene Management Company LLC',
    reportingEntityType: 'Third Party Administrator',
    status: 'ready',
    isLatest: true,
    products: [
      product({}),
      product({ fileId: 2, productKey: 'essential-plan', productLabel: 'Essential Plan', pricesPublished: 379958, uniqueRates: 9497, ...essential }),
    ],
  }
}

function mountLine(summary: SnapshotSummary) {
  return mount(DatasetSnapshot, { props: { snapshot: summary }, global: { plugins: [createVuetify()] } })
}

describe('DatasetSnapshot', () => {
  it('says which files are loaded in one line, with no codes, tax IDs or file names', () => {
    const wrapper = mountLine(snapshot())
    expect(wrapper.get('p').text().replace(/\s+/g, ' ')).toBe('Sep 28, 2026 files · 2 plans · 17,140 prices')
    expect(wrapper.text()).not.toMatch(/snapshot|tax id|npi|\.json|unique|published/i)
    expect(wrapper.find('.problem').exists()).toBe(false)
  })

  it('says when a file is not fully loaded', () => {
    const wrapper = mountLine(snapshot({ status: 'partial' }))
    expect(wrapper.get('.problem').text()).toBe('1 file not fully loaded')
  })
})
