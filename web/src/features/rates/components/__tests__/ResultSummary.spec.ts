import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { LocationOption, RateSearchResult } from '../../api/types'
import { parseRouteQuery } from '../../lib/searchParams'
import ResultSummary from '../ResultSummary.vue'

const locations: LocationOption[] = [
  { label: 'Brooklyn', isBorough: true, npis: 4874, county: 'Kings County' },
  { label: 'Delaware', isBorough: false, npis: 61, county: 'Delaware County' },
]

function result(query: Partial<RateSearchResult['query']>): RateSearchResult {
  return {
    query: {
      code: '45378',
      matchedCodes: ['45378'],
      codeType: null,
      npi: null,
      ein: null,
      product: null,
      setting: null,
      feeType: null,
      provider: null,
      specialty: null,
      county: null,
      countyPlace: null,
      group: null,
      sort: 'rate',
      page: 1,
      pageSize: 25,
      ...query,
    },
    snapshot: { snapshotId: 1, indexDate: '2026-08-31', isLatest: true },
    code: null,
    groups: [],
    rows: { total: 0, page: 1, pageSize: 25, items: [] },
    hints: [],
    quality: { publishedRows: 0, uniqueRates: 0, duplicatesCollapsed: 0, providersWithMultipleRates: 0, maskedTaxIds: 0, wholeTaxIdContracts: 0 },
  }
}

function chips(res: RateSearchResult, query: Record<string, string>) {
  const vuetify = createVuetify({ theme: { defaultTheme: 'light' } })
  const wrapper = mount(ResultSummary, {
    props: { result: res, params: parseRouteQuery({ code: '45378', ...query }), products: [], settings: [], locations },
    global: { plugins: [vuetify] },
  })
  return wrapper.findAll('[data-testid="active-filter"]')
}

describe('ResultSummary', () => {
  it('names the place the location matched, as the API does', () => {
    const [kings] = chips(result({ county: 'Kings County', countyPlace: 'Brooklyn' }), { county: 'Kings County' })
    expect(kings!.text()).toBe('Location: Brooklyn')
    const [delaware] = chips(result({ county: 'Delaware', countyPlace: 'Delaware County' }), { county: 'Delaware' })
    expect(delaware!.text()).toBe('Location: Delaware County')
  })

  it('marks only an NPI, EIN or name chip as the provider searched', () => {
    const all = chips(result({ npi: '1174873780', specialty: 'gastro', county: 'Brooklyn', countyPlace: 'Brooklyn' }), {
      npi: '1174873780',
      specialty: 'gastro',
      county: 'Brooklyn',
    })
    expect(all.map((chip) => chip.classes().includes('copper-mark'))).toEqual([true, false, false])
    // Long specialty names wrap inside the chip, so its remove button stays in view.
    expect(all.every((chip) => chip.classes().includes('active-filter'))).toBe(true)
  })
})
