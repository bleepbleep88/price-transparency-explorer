import { describe, expect, it } from 'vitest'
import {
  CLEANING_RULES,
  cleaningLines,
  contentsLine,
  datesLine,
  fileLinkText,
  fileProblem,
  fileSize,
  kindLabel,
  leadSnapshot,
  namesLine,
  planFiles,
  planProblems,
  priceTotals,
  publishedLine,
  snapshotProblem,
  sourcesSubtitle,
  timingLine,
  totalsText,
} from '../sourceText'
import { ALLOWED_FILE, septemberSnapshot, sourceFile, sourceSnapshot } from './fixtures'

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

  it('gives sizes in decimal MB, and small files in KB rather than "0 MB"', () => {
    // 89,359,830 bytes is 89 MB (85 MiB): the README's sizes are decimal too.
    expect(fileSize(89359830)).toBe('89 MB')
    expect(fileSize(122953)).toBe('123 KB')
    expect(fileSize(300)).toBe('1 KB')
    expect(fileSize(null)).toBe('size unknown')
  })

  it('puts the size on a file link, since the link downloads the whole file', () => {
    expect(fileLinkText('Prices', sourceFile())).toBe('Prices · 89 MB')
    expect(fileLinkText('Out of network', ALLOWED_FILE)).toBe('Out of network · 520 KB')
    expect(fileLinkText('Prices', sourceFile({ contentLength: null }))).toBe('Prices')
  })

  it('gives the one answer: prices in the rate files, and what is left once exact copies count once', () => {
    const totals = priceTotals(septemberSnapshot())
    expect(totals).toEqual({ published: 573393, unique: 17140 })
    expect(totalsText(totals!)).toBe('573,393 prices → 17,140 after removing copies')
    // Allowed-amount files hold amounts, not prices; files not loaded yet count for nothing.
    expect(priceTotals(sourceSnapshot([ALLOWED_FILE]))).toBeNull()
    expect(priceTotals(sourceSnapshot([sourceFile({ counts: null })]))).toBeNull()
  })

  it('leads with the month shown by default, and names the months and who published them', () => {
    const older = { ...sourceSnapshot(), snapshotId: 9, isLatest: false }
    expect(leadSnapshot([older, septemberSnapshot()])?.indexDate).toBe('2026-09-28')
    expect(leadSnapshot([older])?.snapshotId).toBe(9)
    expect(leadSnapshot([])).toBeNull()
    expect(sourcesSubtitle([septemberSnapshot(), older, older])).toBe('3 months of public files from Centene Management Company')
    expect(sourcesSubtitle([{ ...older, reportingEntityName: null }])).toBe('1 month of public files')
    expect(sourcesSubtitle([])).toBeNull()
  })

  it('groups a month by plan, each with its prices file and its out-of-network file', () => {
    const plans = planFiles(septemberSnapshot())
    expect(plans.map((plan) => [plan.productLabel, plan.prices?.fileId, plan.outOfNetwork?.fileId])).toEqual([
      ['Ambetter from Fidelis Care', 11, 13],
      ['Essential Plan', 12, 14],
    ])
    expect(planFiles(sourceSnapshot([ALLOWED_FILE]))[0]).toMatchObject({ productKey: 'essential-plan', prices: null })
  })

  it('says in a few words when a file or a month did not load in full', () => {
    expect(fileProblem(sourceFile())).toBeNull()
    expect(fileProblem(sourceFile({ status: 'partial' }))).toBe('Only partly read')
    expect(fileProblem(sourceFile({ status: 'failed' }))).toBe("Couldn't be loaded")
    expect(fileProblem(sourceFile({ status: 'pending', counts: null }))).toBe('Not loaded yet')
    const [plan] = planFiles(sourceSnapshot([sourceFile({ status: 'partial' }), { ...ALLOWED_FILE, productKey: 'ambetter-from-fidelis-care', status: 'failed' }]))
    expect(planProblems(plan!)).toEqual(['Prices file: only partly read', "Out of network file: couldn't be loaded"])
    expect(planProblems(planFiles(septemberSnapshot())[0]!)).toEqual([])
    expect(snapshotProblem(sourceSnapshot())).toBeNull()
    expect(snapshotProblem({ ...sourceSnapshot(), status: 'failed' })).toBe("This month's list of files couldn't be read")
    expect(snapshotProblem({ ...sourceSnapshot(), status: 'partial' })).toBe('Some files were only partly read')
  })

  it('says when a file mislabels providers, and where the names shown come from instead', () => {
    expect(namesLine(sourceFile())).toBeNull()
    const mislabelled = sourceFile({
      providerNames: {
        agreementPct: 9,
        typicalAgreementPct: 98,
        namesFrom: [
          { productLabel: 'Ambetter from Fidelis Care', indexDate: '2026-09-28' },
          { productLabel: 'Essential Plan', indexDate: '2026-08-31' },
        ],
        unknownTaxIds: 134,
      },
    })
    expect(namesLine(mislabelled)).toBe(
      'Provider names in this file match the other files for only 9% of the same tax IDs (about 98% between the other files). ' +
        "Names shown come from the same tax ID in Ambetter's Sep 28 file, else Essential Plan's Aug 31 file. " +
        `134 names couldn't be found: they show as "Unknown practice".`,
    )
    expect(namesLine(sourceFile({ providerNames: { agreementPct: 9, typicalAgreementPct: null, namesFrom: [], unknownTaxIds: 0 } }))).toBe(
      'Provider names in this file match the other files for only 9% of the same tax IDs. ' +
        'Names shown come from the national provider registry where it names the practice.',
    )
  })

  it('puts the three dates of a file on one line, and keeps every cleaning rule short', () => {
    expect(datesLine(sourceFile())).toBe('List of files Aug 31, 2026 · file says Sep 2, 2026 · server Sep 4, 2026')
    for (const rule of CLEANING_RULES) expect(rule.split(' ').length).toBeLessThanOrEqual(12)
  })
})
