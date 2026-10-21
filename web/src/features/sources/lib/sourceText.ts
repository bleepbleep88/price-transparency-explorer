import { monthDay, plural, shortDate, wholeNumber } from '@/lib/format'
import { planName } from '@/features/rates/lib/answer'
import { providerName } from '@/lib/plainNames'
import type { SourceFile, SourceKind, SourceSnapshot } from '../api/types'

// Plain readings of /api/v1/sources. The main view gets months, plans, links and one total; the rest is for Details.
// Counts of zero are left out.

export function kindLabel(kind: SourceKind): string {
  if (kind === 'in_network') return 'In-network rates'
  if (kind === 'allowed_amount') return 'Out-of-network allowed amounts'
  return kind
}

/** What a published row is called in each kind of file. */
function rowWords(kind: SourceKind): { published: string; unique: string } {
  return kind === 'allowed_amount' ? { published: 'amount', unique: 'different amount' } : { published: 'price', unique: 'unique rate' }
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
    lines.push(`${plural(counts.wholeTaxIdGroups, 'provider group')} ${verb(counts.wholeTaxIdGroups, 'lists', 'list')} no individual NPIs: a contract with the whole tax ID.`)
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

/** How every file is cleaned, short, for the bottom of the page. */
export const CLEANING_RULES: readonly string[] = [
  'Each file is read as a stream and saved in one step.',
  'Exact copies are counted once.',
  'Codes get back dropped leading zeros (490 → 0490).',
  'Revenue-code facility rates take the procedure code in their notes.',
  'Tax IDs with a prefix the IRS never issues are masked.',
  'NPIs that fail the check digit are kept and flagged.',
  'Bad values are set aside and counted, never stopping a file.',
]

/** One plan in one month, with its two files: prices (in-network) and out of network (allowed amounts). */
export interface PlanFiles {
  productKey: string
  productLabel: string
  prices: SourceFile | null
  outOfNetwork: SourceFile | null
}

/** A month's files grouped by plan, in the order the plans first appear. */
export function planFiles(snapshot: SourceSnapshot): PlanFiles[] {
  const plans = new Map<string, PlanFiles>()
  for (const file of snapshot.files) {
    const plan = plans.get(file.productKey) ?? { productKey: file.productKey, productLabel: file.productLabel, prices: null, outOfNetwork: null }
    if (file.kind === 'allowed_amount') plan.outOfNetwork = file
    else plan.prices = file
    plans.set(file.productKey, plan)
  }
  return [...plans.values()]
}

/** Prices in a month's rate files, and how many are left once exact copies count once. Null until one is loaded. */
export function priceTotals(snapshot: SourceSnapshot): { published: number; unique: number } | null {
  const loaded = snapshot.files.filter((file) => file.kind === 'in_network' && file.counts?.published != null && file.counts.unique != null)
  if (!loaded.length) return null
  return loaded.reduce(
    (sum, file) => ({ published: sum.published + file.counts!.published!, unique: sum.unique + file.counts!.unique! }),
    { published: 0, unique: 0 },
  )
}

/** What the big figure's second number is, said next to it. */
export const AFTER_COPIES = 'after removing copies'

/** "573,393 prices → 17,140 after removing copies" */
export function totalsText(totals: { published: number; unique: number }): string {
  return `${plural(totals.published, 'price')} → ${wholeNumber(totals.unique)} ${AFTER_COPIES}`
}

/** A file link with its size, since it downloads the whole file: "Prices · 91 MB". Just the name when the size is unknown. */
export function fileLinkText(name: string, file: SourceFile): string {
  return file.contentLength == null ? name : `${name} · ${fileSize(file.contentLength)}`
}

/** The month whose numbers lead the page: the one shown by default, else the newest. */
export function leadSnapshot(snapshots: readonly SourceSnapshot[]): SourceSnapshot | null {
  return snapshots.find((snapshot) => snapshot.isLatest) ?? snapshots[0] ?? null
}

/** "3 months of public files from Centene Management Company" */
export function sourcesSubtitle(snapshots: readonly SourceSnapshot[]): string | null {
  if (!snapshots.length) return null
  const publisher = snapshots.find((snapshot) => snapshot.reportingEntityName)?.reportingEntityName
  const months = plural(snapshots.length, 'month')
  return publisher ? `${months} of public files from ${providerName(publisher)}` : `${months} of public files`
}

/** A file's trouble in two or three words, for the main view. Null when it loaded in full. */
export function fileProblem(file: SourceFile): string | null {
  if (file.status === 'failed') return "Couldn't be loaded"
  if (file.status === 'partial') return 'Only partly read'
  if (!file.counts) return 'Not loaded yet'
  return null
}

/** "Prices file: only partly read", one line per file of the plan that didn't load in full. */
export function planProblems(plan: PlanFiles): string[] {
  const files: [string, SourceFile | null][] = [
    ['Prices', plan.prices],
    ['Out of network', plan.outOfNetwork],
  ]
  return files.flatMap(([label, file]) => {
    const problem = file ? fileProblem(file) : null
    return problem ? [`${label} file: ${problem.charAt(0).toLowerCase()}${problem.slice(1)}`] : []
  })
}

/** A month's trouble in a few words. Null when it was read. */
export function snapshotProblem(snapshot: SourceSnapshot): string | null {
  if (snapshot.status === 'failed') return "This month's list of files couldn't be read"
  if (snapshot.status === 'partial') return 'Some files were only partly read'
  return null
}

/**
 * For Details: a rate file whose provider names don't match the other files for the same tax IDs, where the names shown
 * come from instead, and how many couldn't be found. Null for a file whose names are used as published.
 */
export function namesLine(file: SourceFile): string | null {
  const names = file.providerNames
  if (!names) return null
  const typical = names.typicalAgreementPct != null ? ` (about ${names.typicalAgreementPct}% between the other files)` : ''
  const from = names.namesFrom.map((ref) => `${planName(ref.productLabel)}'s ${monthDay(ref.indexDate)} file`)
  const unknown = names.unknownTaxIds ? ` ${plural(names.unknownTaxIds, 'name')} couldn't be found: they show as "Unknown practice".` : ''
  return (
    `Provider names in this file match the other files for only ${names.agreementPct}% of the same tax IDs${typical}. ` +
    (from.length
      ? `Names shown come from the same tax ID in ${from.join(', else ')}.`
      : 'Names shown come from the national provider registry where it names the practice.') +
    unknown
  )
}

/** "List of files Aug 31, 2026 · file says Sep 2, 2026 · server Sep 4, 2026" */
export function datesLine(file: SourceFile): string {
  const { indexDate, fileLastUpdatedOn, httpLastModified } = file.dates
  return `List of files ${shortDate(indexDate)} · file says ${shortDate(fileLastUpdatedOn)} · server ${shortDate(httpLastModified)}`
}
