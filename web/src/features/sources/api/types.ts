// Types mirroring the API's source records (src/PriceExplorer.Api/Core/Models/Sources). JSON is camelCase; dates are ISO strings.

/** in_network: negotiated rates. allowed_amount: out-of-network allowed amounts. */
export type SourceKind = 'in_network' | 'allowed_amount' | string

export interface SourceFileDates {
  /** The date in the index file's name (the snapshot). */
  indexDate: string | null
  /** last_updated_on inside the file. */
  fileLastUpdatedOn: string | null
  /** The payer's server's Last-Modified header. */
  httpLastModified: string | null
}

/** What the file held and what loading did. Fields that don't apply to a kind of file are null. */
export interface SourceFileCounts {
  items: number | null
  /** Prices (rate files) or payments (allowed-amount files) as published. */
  published: number | null
  /** Unique rates or unique payments, once exact duplicates collapse. */
  unique: number | null
  duplicatesRemoved: number | null
  rejected: number | null
  /** Provider groups (rate files) or tax ID entries (allowed-amount files). */
  providerGroups: number | null
  distinctNpis: number | null
  distinctEins: number | null
  invalidNpis: number | null
  unverifiedEins: number
  wholeTaxIdGroups: number | null
  danglingReferences: number | null
  codesWithZerosRestored: number
  ratesFromNotes: number | null
  einsWithZeroRestored: number | null
}

export interface SourceReject {
  reason: string
  /** What it is and what loading did about it, in plain words. */
  description: string
  count: number
  /** The first one seen: a JSON path or a value. Tax ID values come masked. */
  example: string | null
}

export interface SourceFileTiming {
  downloadMs: number | null
  parseMs: number | null
  loadedAt: string | null
}

export interface SourceFile {
  fileId: number
  kind: SourceKind
  productKey: string
  productLabel: string
  url: string
  description: string | null
  status: 'pending' | 'loaded' | 'partial' | 'failed' | string
  error: string | null
  warning: string | null
  dates: SourceFileDates
  contentLength: number | null
  sha256: string | null
  eTag: string | null
  fileSchemaVersion: string | null
  reportingEntityName: string | null
  /** Null until the file has been loaded once. */
  counts: SourceFileCounts | null
  rejects: SourceReject[]
  timing: SourceFileTiming
}

export interface SourceSnapshot {
  snapshotId: number
  indexDate: string | null
  indexUrl: string
  indexLastUpdatedOn: string | null
  indexSha256: string | null
  schemaVersion: string | null
  reportingEntityName: string | null
  reportingEntityType: string | null
  status: 'loading' | 'ready' | 'partial' | 'failed' | string
  error: string | null
  isLatest: boolean
  updatedAt: string | null
  files: SourceFile[]
}
