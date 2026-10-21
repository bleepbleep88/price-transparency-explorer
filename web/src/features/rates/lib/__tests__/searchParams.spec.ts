import { describe, expect, it } from 'vitest'
import {
  formValuesFrom,
  mergeQuery,
  npiCheckDigitWarning,
  parseRouteQuery,
  providerIdPatch,
  toApiQuery,
  validateSearchForm,
} from '../searchParams'

describe('parseRouteQuery', () => {
  it('reads every search parameter and trims values', () => {
    const params = parseRouteQuery({
      code: ' 45378 ',
      npi: '1396747861',
      ein: '11-3471596',
      setting: 'asc',
      feeType: 'facility',
      provider: 'nyu',
      specialty: ' gastro ',
      county: 'Manhattan',
      group: 'g1',
      sort: '-rate',
      page: '3',
      pageSize: '50',
      snapshot: '2',
    })
    expect(params).toEqual({
      code: '45378',
      codeType: undefined,
      npi: '1396747861',
      ein: '11-3471596',
      product: undefined,
      setting: 'asc',
      feeType: 'facility',
      provider: 'nyu',
      specialty: 'gastro',
      county: 'Manhattan',
      group: 'g1',
      sort: '-rate',
      page: 3,
      pageSize: 50,
      snapshot: 2,
    })
  })

  it('falls back to defaults for unknown or malformed values', () => {
    const params = parseRouteQuery({ code: ['29881', 'x'], sort: 'price', page: '-2', pageSize: '1000', feeType: 'both', snapshot: 'x' })
    expect(params.code).toBe('29881')
    expect(params.sort).toBe('rate')
    expect(params.page).toBe(1)
    expect(params.pageSize).toBe(25)
    expect(params.feeType).toBeUndefined()
    expect(params.snapshot).toBeUndefined()
  })
})

describe('mergeQuery', () => {
  it('applies a patch, removes nulls, and drops defaults', () => {
    const next = mergeQuery({ code: '45378', ein: '113471596', page: '4' }, { ein: null, setting: 'asc', page: 1, sort: 'rate' })
    expect(next).toEqual({ code: '45378', setting: 'asc' })
  })

  it('leaves keys alone when the patch value is undefined', () => {
    expect(mergeQuery({ code: '45378', npi: '1' }, { npi: undefined })).toEqual({ code: '45378', npi: '1' })
  })

  it('sets and clears the specialty and location like any other filter', () => {
    const filtered = mergeQuery({ code: '45378' }, { specialty: 'gastro', county: 'Manhattan', page: 1 })
    expect(filtered).toEqual({ code: '45378', specialty: 'gastro', county: 'Manhattan' })
    // A hint's "Clear the location filter" link sends { county: null }.
    const cleared = mergeQuery({ code: '45378', specialty: 'gastro', county: 'Manhattan' }, { county: null, group: null })
    expect(cleared).toEqual({ code: '45378', specialty: 'gastro' })
  })
})

describe('lens and cases', () => {
  it('reads a known lens and a case count, and drops anything else', () => {
    expect(parseRouteQuery({ code: '45378', lens: 'employers', cases: '1200' })).toMatchObject({ lens: 'employers', cases: 1200 })
    expect(parseRouteQuery({ code: '45378', lens: 'life-sciences' }).lens).toBe('life-sciences')
    const bad = parseRouteQuery({ code: '45378', lens: 'payers', cases: '-3' })
    expect(bad.lens).toBeUndefined()
    expect(bad.cases).toBeUndefined()
    expect(parseRouteQuery({ code: '45378', cases: '2000000' }).cases).toBeUndefined()
  })

  it('never sends the lens or the case count to the API', () => {
    const query = toApiQuery(parseRouteQuery({ code: '45378', lens: 'plans', cases: '10' }))
    expect(query).not.toHaveProperty('lens')
    expect(query).not.toHaveProperty('cases')
  })

  it('drops the case count for a different code, and keeps it otherwise', () => {
    expect(mergeQuery({ code: '45378', lens: 'employers', cases: '1200' }, { code: '66984', page: null })).toEqual({ code: '66984', lens: 'employers' })
    expect(mergeQuery({ code: '45378', cases: '1200' }, { code: '45378', npi: '1396747861' })).toMatchObject({ cases: '1200' })
    expect(mergeQuery({ code: '45378', cases: '1200' }, { lens: 'plans' })).toMatchObject({ cases: '1200' })
    expect(mergeQuery({ code: '45378', cases: '1200' }, { code: '66984', cases: 10 })).toMatchObject({ code: '66984', cases: '10' })
  })

  it('keeps the lens when a new search replaces the code', () => {
    const next = mergeQuery({ code: '45378', lens: 'plans', npi: '1396747861' }, { code: '99213', npi: null, page: null })
    expect(next).toEqual({ code: '99213', lens: 'plans' })
  })
})

describe('toApiQuery', () => {
  it('sends only what the API understands', () => {
    const query = toApiQuery(parseRouteQuery({ code: '45378', page: '2' }))
    expect(query).toMatchObject({ code: '45378', page: 2, pageSize: 25, sort: 'rate' })
  })

  it('passes the specialty and location through as typed', () => {
    const query = toApiQuery(parseRouteQuery({ code: '45378', specialty: 'Gastroenterology', county: 'Monroe' }))
    expect(query).toMatchObject({ specialty: 'Gastroenterology', county: 'Monroe' })
    expect(toApiQuery(parseRouteQuery({ code: '45378' }))).toMatchObject({ specialty: undefined, county: undefined })
  })
})

describe('validateSearchForm', () => {
  const valid = { code: '45378', npi: '', ein: '', provider: '', specialty: '', county: '' }

  it('accepts a code alone', () => {
    expect(validateSearchForm(valid)).toEqual({})
  })

  it('requires a code with a helpful message', () => {
    expect(validateSearchForm({ ...valid, code: '  ' }).code).toMatch(/Type a service/)
  })

  it.each([
    ['code', '!!!', /Pick a service/],
    ['code', '123456789', /Pick a service/],
    ['npi', '12345', /10 digits/],
    ['npi', '13967478AB', /10 digits/],
    ['ein', '1234', /9 digits/],
    ['provider', 'a', /at least 2/],
    ['specialty', 'g', /at least 2 characters of a specialty/],
    ['specialty', 'g'.repeat(101), /at most 100/],
    ['county', 'x', /New York county or NYC borough/],
    ['county', 'x'.repeat(61), /at most 60/],
  ] as const)('rejects %s = %s', (field, value, message) => {
    expect(validateSearchForm({ ...valid, [field]: value })[field]).toMatch(message)
  })

  it.each(['11-3471596', '113471596', 'u0123456789ab'])('accepts EIN or provider key %s', (ein) => {
    expect(validateSearchForm({ ...valid, ein })).toEqual({})
  })

  it('accepts the specialty and location when set or left out', () => {
    expect(validateSearchForm({ ...valid, specialty: 'gastro', county: 'Staten Island' })).toEqual({})
  })

  it('round-trips params into form values', () => {
    expect(formValuesFrom(parseRouteQuery({ code: '45378', npi: '1396747861', county: 'Bronx' }))).toEqual({
      code: '45378',
      npi: '1396747861',
      ein: '',
      provider: '',
      specialty: '',
      county: 'Bronx',
    })
  })
})

describe('npiCheckDigitWarning', () => {
  it('warns (without blocking) on a 10-digit NPI that fails the check digit', () => {
    expect(npiCheckDigitWarning('1861171663')).toMatch(/fails the standard check-digit test/)
    expect(npiCheckDigitWarning('1396747861')).toBeNull()
    expect(npiCheckDigitWarning('123')).toBeNull()
  })
})

describe('providerIdPatch', () => {
  it('reads 10 digits as an NPI and 9 as a tax ID, with the form rules, and clears the other', () => {
    expect(providerIdPatch(' 1043383789 ')).toEqual({ npi: '1043383789', ein: null })
    expect(providerIdPatch('11-3466729')).toEqual({ ein: '113466729', npi: null })
    expect(providerIdPatch('113466729')).toEqual({ ein: '113466729', npi: null })
  })

  it('gives nothing for anything else', () => {
    for (const text of ['', '   ', '12345', '1043383789012', 'abc', '11-346672x', null]) {
      expect(providerIdPatch(text)).toBeNull()
    }
  })
})
