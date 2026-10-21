import { describe, expect, it } from 'vitest'
import { compareApiQuery, gapApiQuery, mergeInsightQuery, parseCompareQuery, parseGapQuery, parseScorecardQuery, pickProducts } from '../insightParams'

describe('parseGapQuery', () => {
  it('reads the filters and falls back to defaults for anything malformed', () => {
    expect(parseGapQuery({ product: 'essential-plan', feeType: 'facility', code: ' CPT 45378 ', sort: 'spread', page: '3', pageSize: '50' })).toEqual({
      product: 'essential-plan',
      category: undefined,
      setting: undefined,
      feeType: 'facility',
      code: 'CPT 45378',
      sort: 'spread',
      page: 3,
      pageSize: 50,
      snapshot: undefined,
    })
    expect(parseGapQuery({ feeType: 'institutional', sort: 'savings', page: '-1', pageSize: '500', snapshot: 'x' })).toMatchObject({
      feeType: undefined,
      sort: 'gap',
      page: 1,
      pageSize: 25,
      snapshot: undefined,
    })
  })

  it('sends only what the API understands', () => {
    expect(gapApiQuery(parseGapQuery({ code: '0490', snapshot: '2' }))).toMatchObject({ code: '0490', sort: 'gap', page: 1, pageSize: 25, snapshot: 2 })
  })
})

describe('parseCompareQuery', () => {
  it('reads both products and the order', () => {
    expect(parseCompareQuery({ product: 'ambetter', versus: 'essential-plan', sort: '-ratio', setting: 'asc' })).toMatchObject({
      product: 'ambetter',
      versus: 'essential-plan',
      sort: '-ratio',
      setting: 'asc',
    })
    expect(parseCompareQuery({ sort: 'cheapest' }).sort).toBe('difference')
    expect(compareApiQuery(parseCompareQuery({ code: '99213' }))).toMatchObject({ code: '99213', sort: 'difference' })
  })

  it('shows the biggest differences ten at a time unless the address says otherwise', () => {
    expect(parseCompareQuery({}).pageSize).toBe(10)
    expect(parseCompareQuery({ pageSize: '25' }).pageSize).toBe(25)
  })
})

describe('parseScorecardQuery', () => {
  it('keeps the name the reader came from, cut to a sane length', () => {
    expect(parseScorecardQuery({ as: ' The Trustees of Columbia ' }).as).toBe('The Trustees of Columbia')
    expect(parseScorecardQuery({ as: 'x'.repeat(500) }).as).toHaveLength(200)
    expect(parseScorecardQuery({}).as).toBeUndefined()
  })
})

describe('mergeInsightQuery', () => {
  it('removes cleared filters, drops defaults and starts again at page 1', () => {
    expect(mergeInsightQuery({ code: '99213', page: '4', category: 'visits' }, { category: null }, 'gap')).toEqual({ code: '99213' })
    expect(mergeInsightQuery({ sort: 'spread' }, { sort: 'gap', pageSize: 25 }, 'gap')).toEqual({})
  })

  it('keeps a page the patch sets', () => {
    expect(mergeInsightQuery({ code: '99213' }, { page: 2 }, 'gap')).toEqual({ code: '99213', page: '2' })
    expect(mergeInsightQuery({ page: '2' }, { page: 1, pageSize: 50 }, 'difference')).toEqual({ pageSize: '50' })
  })

  it("drops the page size only when it's the page's own default", () => {
    expect(mergeInsightQuery({}, { pageSize: 10 }, 'difference', 10)).toEqual({})
    expect(mergeInsightQuery({}, { pageSize: 25 }, 'difference', 10)).toEqual({ pageSize: '25' })
  })
})

describe('pickProducts', () => {
  const current = { product: 'essential-plan', versus: 'ambetter' }

  it('changes one side', () => {
    expect(pickProducts(current, 'product', 'child-health-plus')).toEqual({ product: 'child-health-plus', versus: 'ambetter' })
    expect(pickProducts(current, 'versus', 'child-health-plus')).toEqual({ product: 'essential-plan', versus: 'child-health-plus' })
  })

  it("swaps the two when a side picks the other side's product", () => {
    expect(pickProducts(current, 'product', 'ambetter')).toEqual({ product: 'ambetter', versus: 'essential-plan' })
    expect(pickProducts(current, 'versus', 'essential-plan')).toEqual({ product: 'ambetter', versus: 'essential-plan' })
  })
})
