import { describe, expect, it } from 'vitest'
import { cleaningLines, contentsLine, fileSize, kindLabel, publishedLine, timingLine } from '../sourceText'
import { ALLOWED_FILE, sourceFile } from './fixtures'

describe('source readings', () => {
  it('names each kind of file and what a published row is in it', () => {
    expect(kindLabel('in_network')).toBe('In-network rates')
    expect(kindLabel('allowed_amount')).toBe('Out-of-network allowed amounts')
    expect(publishedLine(sourceFile())).toBe('179,948 prices in the file became 7,727 unique rates after removing exact copies.')
    expect(publishedLine(ALLOWED_FILE)).toBe('1,033 amounts in the file became 605 different amounts after removing exact copies.')
    expect(publishedLine(sourceFile({ counts: null }))).toBeNull()
  })

  it('says how a rate file was cleaned, one line per rule that changed something', () => {
    expect(cleaningLines(sourceFile())).toEqual([
      '119 codes published without their leading zeros, restored (490 → 0490).',
      '1,867 facility rates name the procedure in the notes field; that code is the one searched.',
      '326 tax IDs use a prefix the IRS never issues, so they are masked.',
      '1 NPI fails the check digit: kept and flagged.',
      '5 provider groups list no individual NPIs: a contract with the whole tax ID.',
    ])
  })

  it('says how an allowed-amount file was cleaned, leaving out what does not apply or did not happen', () => {
    expect(cleaningLines(ALLOWED_FILE)).toEqual([
      '15 codes published without their leading zeros, restored (490 → 0490).',
      '6 EINs published as 8 digits, given back their leading zero.',
    ])
    expect(cleaningLines(sourceFile({}, { duplicatesRemoved: 0, codesWithZerosRestored: 1, ratesFromNotes: 0, unverifiedEins: 1, invalidNpis: 0, wholeTaxIdGroups: 0, danglingReferences: 1 }))).toEqual([
      '1 code published without its leading zeros, restored (490 → 0490).',
      '1 tax ID uses a prefix the IRS never issues, so it is masked.',
      '1 provider reference is used by rates but never defined in the file.',
    ])
  })

  it('sums up contents and timing', () => {
    expect(contentsLine(sourceFile())).toBe('1,519 items · 8,030 provider groups · 5,739 EINs · 43,833 NPIs')
    expect(contentsLine(ALLOWED_FILE)).toBe('150 items · 427 tax ID entries · 127 EINs · 147 NPIs')
    expect(timingLine(sourceFile())).toBe('Fetched (or re-checked) in 18 ms, read in 736 ms, saved Oct 20, 2026.')
    expect(timingLine(ALLOWED_FILE)).toBe('Fetched (or re-checked) in 1.2 s, read in 4 ms, saved Oct 20, 2026.')
    expect(timingLine(sourceFile({ timing: { downloadMs: null, parseMs: null, loadedAt: null } }))).toBeNull()
  })

  it('gives small files their size in KB rather than "0 MB"', () => {
    expect(fileSize(89359830)).toBe('85 MB')
    expect(fileSize(122953)).toBe('120 KB')
    expect(fileSize(300)).toBe('1 KB')
    expect(fileSize(null)).toBe('size unknown')
  })
})
