import type { SourceFile, SourceFileCounts, SourceSnapshot } from '../../api/types'

// Counts as /api/v1/sources returned them for the real 2026-08-31 Ambetter files.
export function sourceFile(overrides: Partial<SourceFile> = {}, counts: Partial<SourceFileCounts> = {}): SourceFile {
  return {
    fileId: 1,
    kind: 'in_network',
    productKey: 'ambetter-from-fidelis-care',
    productLabel: 'Ambetter from Fidelis Care',
    url: 'https://www.centene.com/2026-08-31_centene-management-company-llc_fidelis-ex_in-network.json',
    description: 'in-network file',
    status: 'loaded',
    error: null,
    warning: null,
    dates: { indexDate: '2026-08-31', fileLastUpdatedOn: '2026-09-02', httpLastModified: '2026-09-04T18:23:42+00:00' },
    contentLength: 89359830,
    sha256: '0d0afd0fbf14a544004d4dc797a357257e0677bf36330e72673c1c0fbcc10214',
    eTag: '"0x8DF0AB1A6895333"',
    fileSchemaVersion: '2.0.0',
    reportingEntityName: 'Centene Management Company LLC',
    counts: {
      items: 1519,
      published: 179948,
      unique: 7727,
      duplicatesRemoved: 172221,
      rejected: 0,
      providerGroups: 8030,
      distinctNpis: 43833,
      distinctEins: 5739,
      invalidNpis: 1,
      unverifiedEins: 326,
      wholeTaxIdGroups: 5,
      danglingReferences: 0,
      codesWithZerosRestored: 119,
      ratesFromNotes: 1867,
      einsWithZeroRestored: null,
      ...counts,
    },
    rejects: [],
    timing: { downloadMs: 18, parseMs: 736, loadedAt: '2026-10-20T09:40:03.739284+00:00' },
    ...overrides,
  }
}

export const ALLOWED_FILE = sourceFile(
  {
    fileId: 4,
    kind: 'allowed_amount',
    productKey: 'essential-plan',
    productLabel: 'Essential Plan',
    url: 'https://www.centene.com/2026-08-31_centene-management-company-llc_fidelis-es_allowed-amounts.json',
    contentLength: 520468,
    timing: { downloadMs: 1240, parseMs: 4, loadedAt: '2026-10-20T09:40:05.198584+00:00' },
  },
  {
    items: 150,
    published: 1033,
    unique: 605,
    duplicatesRemoved: 428,
    providerGroups: 427,
    distinctNpis: 147,
    distinctEins: 127,
    invalidNpis: 0,
    unverifiedEins: 0,
    wholeTaxIdGroups: null,
    danglingReferences: null,
    codesWithZerosRestored: 15,
    ratesFromNotes: null,
    einsWithZeroRestored: 6,
  },
)

/** The real 2026-08-31 snapshot's shape: a rate file and an allowed-amount file, newest snapshot first. */
export function sourceSnapshot(files: SourceFile[] = [sourceFile(), ALLOWED_FILE]): SourceSnapshot {
  return {
    snapshotId: 1,
    indexDate: '2026-08-31',
    indexUrl: 'https://www.centene.com/2026-08-31_fidelis_index.json',
    indexLastUpdatedOn: '2026-08-31',
    indexSha256: 'bb3a34c9f4194bc5e4e04e612b71979aa939eb3c15ca49e8f22774d8b9967f84',
    schemaVersion: '2.0.0',
    reportingEntityName: 'Centene Management Company LLC',
    reportingEntityType: 'Third Party Administrator',
    status: 'ready',
    error: null,
    isLatest: true,
    updatedAt: '2026-10-20T09:40:05.204528+00:00',
    files,
  }
}

/** The real 2026-09-28 snapshot's in-network totals: 193,435 + 379,958 prices → 7,643 + 9,497 unique. */
export function septemberSnapshot(): SourceSnapshot {
  const ambetter = sourceFile({ fileId: 11, dates: { indexDate: '2026-09-28', fileLastUpdatedOn: '2026-10-06', httpLastModified: '2026-10-08T17:48:22+00:00' } }, { published: 193435, unique: 7643 })
  const essential = sourceFile(
    { fileId: 12, productKey: 'essential-plan', productLabel: 'Essential Plan', url: 'https://www.centene.com/2026-09-28_centene-management-company-llc_fidelis-es_in-network.json' },
    { published: 379958, unique: 9497 },
  )
  const ambetterOon = { ...ALLOWED_FILE, fileId: 13, productKey: 'ambetter-from-fidelis-care', productLabel: 'Ambetter from Fidelis Care' }
  const essentialOon = { ...ALLOWED_FILE, fileId: 14 }
  return { ...sourceSnapshot([ambetter, essential, ambetterOon, essentialOon]), snapshotId: 2, indexDate: '2026-09-28', indexLastUpdatedOn: '2026-09-28' }
}
