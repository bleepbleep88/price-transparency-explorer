import { describe, expect, it } from 'vitest'
import { mergeChangesQuery, parseChangesQuery, toChangesApiQuery } from '../changesParams'

describe('changes URL state', () => {
  it('reads every filter from the URL and falls back to defaults for bad values', () => {
    expect(
      parseChangesQuery({
        snapshot: '1',
        from: '3',
        product: 'essential-plan',
        code: '99213',
        kind: 'changed',
        minChangePct: '5',
        listedElsewhere: 'false',
        includeRevenueCodeOnly: 'true',
        sort: '-changePct',
        page: '2',
        pageSize: '50',
      }),
    ).toEqual({
      snapshot: 1,
      from: 3,
      product: 'essential-plan',
      code: '99213',
      kind: 'changed',
      minChangePct: 5,
      hideListedElsewhere: true,
      includeRevenueCodeOnly: true,
      sort: '-changePct',
      page: 2,
      pageSize: 50,
    })
    expect(parseChangesQuery({ snapshot: '-1', kind: 'raised', minChangePct: '0', sort: 'rate', page: 'x', pageSize: '500' })).toEqual({
      snapshot: undefined,
      from: undefined,
      product: undefined,
      code: undefined,
      kind: undefined,
      minChangePct: undefined,
      hideListedElsewhere: false,
      includeRevenueCodeOnly: false,
      sort: 'largest',
      page: 1,
      pageSize: 25,
    })
  })

  it('takes the went-up and went-down filters', () => {
    expect(parseChangesQuery({ kind: 'increased' }).kind).toBe('increased')
    expect(parseChangesQuery({ kind: 'decreased' }).kind).toBe('decreased')
  })

  it('keeps URLs short: defaults and cleared filters drop out', () => {
    expect(mergeChangesQuery({ code: '45378', page: '3', snapshot: '2' }, { code: null, sort: 'largest', page: 1, hideListedElsewhere: true })).toEqual({
      snapshot: '2',
      listedElsewhere: 'false',
    })
    expect(mergeChangesQuery({ listedElsewhere: 'false', kind: 'new' }, { hideListedElsewhere: false, kind: undefined, pageSize: 25 })).toEqual({
      kind: 'new',
    })
    expect(mergeChangesQuery({ code: '0450' }, { includeRevenueCodeOnly: true })).toEqual({ code: '0450', includeRevenueCodeOnly: 'true' })
    expect(mergeChangesQuery({ includeRevenueCodeOnly: 'true' }, { includeRevenueCodeOnly: false })).toEqual({})
  })

  it("sends the page's snapshot as the newer file and only what the API takes", () => {
    const query = toChangesApiQuery(parseChangesQuery({ snapshot: '1', from: '2', code: 'RC 0490', listedElsewhere: 'false' }))
    expect(query).toEqual({
      from: 2,
      to: 1,
      product: undefined,
      code: 'RC 0490',
      kind: undefined,
      minChangePct: undefined,
      listedElsewhere: false,
      includeRevenueCodeOnly: undefined,
      sort: undefined,
      page: 1,
      pageSize: 25,
    })
  })
})
