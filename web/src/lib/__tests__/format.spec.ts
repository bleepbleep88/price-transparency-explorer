import { describe, expect, it } from 'vitest'
import { megabytes, money, ordinal, plural, shortDate, times, wholeDollars, wholeNumber } from '../format'

describe('format', () => {
  it('formats money with cents and a dash for missing values', () => {
    expect(money(1154.16)).toBe('$1,154.16')
    expect(money(0.02)).toBe('$0.02')
    expect(money(null)).toBe('—')
  })

  it('formats counts, plurals and multiples', () => {
    expect(wholeNumber(179948)).toBe('179,948')
    expect(plural(1, 'provider')).toBe('1 provider')
    expect(plural(31, 'provider')).toBe('31 providers')
    expect(times(0.6312)).toBe('0.63×')
    expect(times(null)).toBe('—')
  })

  it.each([
    [1, '1st'],
    [2, '2nd'],
    [3, '3rd'],
    [7, '7th'],
    [11, '11th'],
    [13, '13th'],
    [21, '21st'],
    [100, '100th'],
  ])('ordinal(%i) = %s', (value, expected) => {
    expect(ordinal(value)).toBe(expected)
  })

  it('formats calendar dates without shifting the day', () => {
    expect(shortDate('2026-08-31')).toBe('Aug 31, 2026')
    expect(shortDate('2026-09-04T18:23:42')).toBe('Sep 4, 2026')
    expect(shortDate(null)).toBe('—')
  })

  it('shows file sizes in megabytes', () => {
    expect(megabytes(89_359_830)).toBe('85 MB')
  })
})

describe('wholeDollars', () => {
  it('rounds estimates to whole dollars', () => {
    expect(wholeDollars(148632.0)).toBe('$148,632')
    expect(wholeDollars(57599.6)).toBe('$57,600')
    expect(wholeDollars(null)).toBe('—')
  })
})
