import { describe, expect, it } from 'vitest'
import { formatEin, formatNpi, isEinOrProviderKey, isValidNpi, normalizeEin, npiDigits } from '../providerIds'

describe('isValidNpi', () => {
  it.each([
    ['1396747861', true], // from the Fidelis file
    ['1234567893', true], // CMS check-digit example
    ['1396-747-861', true],
    ['1861171663', false], // published in the Essential Plan file, fails the check digit
    ['124507857', false], // nine digits
    ['3396747861', false], // must start with 1 or 2
    ['', false],
  ])('%s → %s', (npi, expected) => {
    expect(isValidNpi(npi)).toBe(expected)
  })
})

describe('EIN helpers', () => {
  it('normalizes the common ways an EIN is written', () => {
    expect(normalizeEin('11-3471596')).toBe('113471596')
    expect(normalizeEin('11 3471596')).toBe('113471596')
    expect(normalizeEin('1134715')).toBeNull()
    expect(formatEin('113471596')).toBe('11-3471596')
  })

  it('accepts provider keys used for masked tax IDs', () => {
    expect(isEinOrProviderKey('u0123456789AB')).toBe(true)
    expect(isEinOrProviderKey('abc')).toBe(false)
  })

  it('strips everything but digits from NPI input', () => {
    expect(npiDigits(' 1396-747-861 ')).toBe('1396747861')
  })
})

describe('formatNpi', () => {
  it('puts back the leading zero a number drops, so a copied NPI searches', () => {
    expect(formatNpi(124507857)).toBe('0124507857')
    expect(formatNpi(1801474036)).toBe('1801474036')
  })
})
