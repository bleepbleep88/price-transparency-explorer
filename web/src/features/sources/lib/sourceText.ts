import { plural, shortDate, wholeNumber } from '@/lib/format'
import type { SourceFile, SourceKind } from '../api/types'

// Plain readings of /api/v1/sources. Counts of zero are left out.

export function kindLabel(kind: SourceKind): string {
  if (kind === 'in_network') return 'In-network rates'
  if (kind === 'allowed_amount') return 'Out-of-network allowed amounts'
  return kind
}

/** What a published row is called in each kind of file. */
function rowWords(kind: SourceKind): { published: string; unique: string } {
  return kind === 'allowed_amount' ? { published: 'payment', unique: 'unique payment' } : { published: 'price', unique: 'unique rate' }
}

/** "179,948 prices in the file became 7,727 unique rates after removing exact copies." */
export function publishedLine(file: SourceFile): string | null {
  const counts = file.counts
  if (counts?.published == null || counts.unique == null) return null
  const words = rowWords(file.kind)
  return `${plural(counts.published, words.published)} in the file became ${plural(counts.unique, words.unique)} after removing exact copies.`
}

function verb(count: number, one: string, many: string): string {
  return count === 1 ? one : many
}

/** How the file was cleaned beyond removing exact copies (publishedLine says that), one line per rule that changed something. */
export function cleaningLines(file: SourceFile): string[] {
  const counts = file.counts
  if (!counts) return []
  const lines: string[] = []
  if (counts.codesWithZerosRestored)
    lines.push(`${plural(counts.codesWithZerosRestored, 'code')} published without ${verb(counts.codesWithZerosRestored, 'its', 'their')} leading zeros, restored (490 → 0490).`)
  if (counts.ratesFromNotes)
    lines.push(`${plural(counts.ratesFromNotes, 'facility rate')} ${verb(counts.ratesFromNotes, 'names', 'name')} the procedure in the notes field; that code is the one searched.`)
  if (counts.einsWithZeroRestored)
    lines.push(`${plural(counts.einsWithZeroRestored, 'EIN')} published as 8 digits, given back ${verb(counts.einsWithZeroRestored, 'its', 'their')} leading zero.`)
  if (counts.unverifiedEins)
    lines.push(`${plural(counts.unverifiedEins, 'tax ID')} ${verb(counts.unverifiedEins, 'uses', 'use')} a prefix the IRS never issues, so ${verb(counts.unverifiedEins, 'it is', 'they are')} masked.`)
  if (counts.invalidNpis)
    lines.push(`${plural(counts.invalidNpis, 'NPI')} ${verb(counts.invalidNpis, 'fails', 'fail')} the check digit: kept and flagged.`)
  if (counts.wholeTaxIdGroups)
    lines.push(`${plural(counts.wholeTaxIdGroups, 'provider group')} ${verb(counts.wholeTaxIdGroups, 'lists', 'list')} npi [0]: a contract with the whole tax ID.`)
  if (counts.danglingReferences)
    lines.push(`${plural(counts.danglingReferences, 'provider reference')} ${verb(counts.danglingReferences, 'is', 'are')} used by rates but never defined in the file.`)
  return lines
}

/** "8,030 provider groups · 5,739 tax IDs · 43,833 NPIs" (allowed-amount files list tax ID entries, not groups). */
export function contentsLine(file: SourceFile): string | null {
  const counts = file.counts
  if (!counts) return null
  const parts: string[] = []
  if (counts.items != null) parts.push(plural(counts.items, 'item'))
  if (counts.providerGroups != null)
    parts.push(file.kind === 'allowed_amount' ? plural(counts.providerGroups, 'tax ID entry', 'tax ID entries') : plural(counts.providerGroups, 'provider group'))
  if (counts.distinctEins != null) parts.push(`${wholeNumber(counts.distinctEins)} EINs`)
  if (counts.distinctNpis != null) parts.push(`${wholeNumber(counts.distinctNpis)} NPIs`)
  return parts.length ? parts.join(' · ') : null
}

function seconds(ms: number): string {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
}

/** "Fetched in 18 ms, read in 736 ms, saved Oct 20, 2026." A cached copy is only re-checked, so fetching can be quick. */
export function timingLine(file: SourceFile): string | null {
  const { downloadMs, parseMs, loadedAt } = file.timing
  if (loadedAt == null) return null
  const parts: string[] = []
  if (downloadMs != null) parts.push(`fetched (or re-checked) in ${seconds(downloadMs)}`)
  if (parseMs != null) parts.push(`read in ${seconds(parseMs)}`)
  parts.push(`saved ${shortDate(loadedAt)}`)
  const text = parts.join(', ')
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`
}

/** "85 MB", or "120 KB" for the small allowed-amount files. */
export function fileSize(bytes: number | null | undefined): string {
  if (bytes == null) return 'size unknown'
  return bytes < 1_048_576 ? `${wholeNumber(Math.max(1, Math.round(bytes / 1024)))} KB` : `${wholeNumber(Math.round(bytes / 1_048_576))} MB`
}

export const STATUS_COLORS: Readonly<Record<string, string>> = {
  loaded: 'success',
  ready: 'success',
  partial: 'warning',
  loading: 'info',
  pending: 'default',
  failed: 'error',
}

/** How every file is cleaned, for the top of the page. */
export const CLEANING_RULES: readonly string[] = [
  'Every file is read as a stream, and each is saved in one step, so a search never sees half a file.',
  'Exact copies are removed; each rate keeps a count of its copies.',
  'Codes get back the leading zeros the file dropped (490 → 0490); the code as published is kept.',
  'Facility rates filed under a revenue code take the procedure code named in their notes.',
  'Tax IDs with a prefix the IRS never issues are masked: they may be Social Security numbers.',
  'NPIs that fail the check digit are kept and flagged.',
  'A bad value never stops a file. It is set aside and counted below, with the reason and the first example.',
]
