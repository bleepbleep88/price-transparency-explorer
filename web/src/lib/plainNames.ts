// Plain names for the main view (decision 90): what a five-year-old would call a place, a bill or a provider.
// The API keeps the precise labels; Details panels can still show them.

import type { ModifierRef } from '@/features/rates/api/types'
import { ALIASES, CODE_NAMES } from './codeNames'

const SETTING_NAMES: Record<string, string> = {
  asc: 'Surgery center',
  'hospital-outpatient': 'Hospital',
  er: 'Emergency room',
  office: "Doctor's office",
  fqhc: 'Health center',
  rhc: 'Rural clinic',
  'telehealth-home': 'Video visit',
  'telehealth-other': 'Video visit',
  lab: 'Lab',
  'urgent-care': 'Urgent care',
  home: 'At home',
  inpatient: 'Hospital stay',
  other: 'Other place',
  any: 'Anywhere',
  unspecified: 'Place not stated',
}

/** Where the care happens: "Surgery center", "Hospital", "Emergency room", "Video visit". */
export function settingName(settingKey: string, fallback?: string): string {
  return SETTING_NAMES[settingKey] ?? fallback ?? 'Other place'
}

/** Settings paid one all-inclusive price per visit. */
export const ALL_IN: ReadonlySet<string> = new Set(['fqhc', 'rhc'])

const DOCTOR_WHERE: Record<string, string> = {
  asc: 'at a surgery center',
  'hospital-outpatient': 'at a hospital',
  er: 'in the emergency room',
  office: 'in the office',
  fqhc: 'at a health center',
  rhc: 'at a rural clinic',
  'telehealth-home': 'by video, at home',
  'telehealth-other': 'by video',
  lab: 'at a lab',
  'urgent-care': 'at urgent care',
  home: 'at home',
  inpatient: 'in the hospital',
}

/** Visit codes: office, ER and other E/M visits, therapy sessions, and the health-center visit codes. */
export function isVisitCode(code: string | undefined): boolean {
  return !!code && /^(99[2-4]\d\d|908\d\d|T1015|G046[6-9]|G0470)$/i.test(code)
}

interface BillGroup {
  feeType: string
  settingKey: string
  settingLabel?: string
  code?: string
}

/**
 * The bill a price is for, as a tile names it. A facility fee is the place's bill ("Surgery center"); a professional
 * fee is the doctor's ("Doctor", with where in `where`), or the therapist's for physical and occupational therapy.
 */
export function billName(group: BillGroup): { name: string; where: string | null } {
  if (group.feeType === 'professional') {
    const who = group.code && /^97\d{3}$/.test(group.code) ? 'Therapist' : 'Doctor'
    return { name: who, where: DOCTOR_WHERE[group.settingKey] ?? null }
  }
  // A health center's visit is paid one all-in price, not a bill on top of the doctor's. Only said for visit codes: a
  // $1.49 lab test at a health center isn't a visit.
  if (ALL_IN.has(group.settingKey) && isVisitCode(group.code)) {
    return { name: `${settingName(group.settingKey, group.settingLabel)} visit (all in one price)`, where: null }
  }
  return { name: settingName(group.settingKey, group.settingLabel), where: null }
}

/** One short line for a bill in a list: "Doctor · in the office", "Surgery center". */
export function billLine(group: BillGroup & { modifiers?: ModifierRef[] }): string {
  const { name, where } = billName(group)
  const extra = modifierWords(group.modifiers ?? [])
  return [name, where, extra].filter(Boolean).join(' · ')
}

const MODIFIER_WORDS: Record<string, string> = {
  '25': 'same day as a procedure',
  '26': 'reading only',
  TC: 'scan only',
  '33': 'preventive',
  '50': 'both sides',
  '51': 'more than one procedure',
  '59': 'separate procedure',
  XE: 'separate procedure',
  XS: 'separate procedure',
  XP: 'separate procedure',
  XU: 'separate procedure',
  '95': 'video',
  GT: 'video',
  GQ: 'video',
  '93': 'by phone',
  FQ: 'by phone',
  QW: 'quick test',
  SU: 'office equipment',
  LT: 'left',
  RT: 'right',
  JW: 'thrown-away drug',
  JZ: 'no drug thrown away',
  GC: 'with a resident',
  '80': 'assistant',
  '82': 'assistant',
  AS: 'assistant',
  QK: 'supervising',
  QX: 'supervised',
  QZ: 'nurse anesthetist',
  AA: 'anesthesiologist',
}

/** Modifiers in a few plain words, no codes: "preventive", "left, right", "video". Empty without any. */
export function modifierWords(modifiers: ModifierRef[]): string {
  const words = modifiers.map(
    (modifier) =>
      MODIFIER_WORDS[modifier.code.toUpperCase()] ??
      modifier.label.replace(/\s*\(modifier [^)]*\)\s*/i, '').trim().replace(/^./, (first) => first.toLowerCase()),
  )
  return [...new Set(words.filter(Boolean))].join(', ')
}


const SMALL_WORDS = new Set(['a', 'an', 'and', 'at', 'by', 'for', 'in', 'of', 'on', 'or', 'the', 'to', 'with', 'w/', 'w/o'])
const KEEP_UPPER = new Set(['CT', 'MRI', 'PET', 'ER', 'IV', 'GI', 'US', 'EKG', 'ECG', 'HIV', 'COVID', 'DNA', 'RNA', 'ASC', 'NY', 'NYC'])

/** Turns an all-capitals name (HCPCS short names are) into sentence case, keeping acronyms such as CT and MRI; a name with lower-case letters is kept as published. */
export function sentenceCase(text: string): string {
  if (/[a-z]/.test(text)) return text
  const words = text.toLowerCase().split(/(\s+|\/|-)/)
  return words
    .map((word, index) => {
      if (KEEP_UPPER.has(word.toUpperCase())) return word.toUpperCase()
      if (index === 0) return word.charAt(0).toUpperCase() + word.slice(1)
      return word
    })
    .join('')
}

const ABBREVIATIONS: Record<string, string> = {
  anes: 'anesthesia',
  upr: 'upper',
  lwr: 'lower',
  ndsc: 'endoscopy',
  px: 'procedure',
  nos: '',
  scr: 'screening',
  mammo: 'mammogram',
  bi: 'both sides',
  incl: 'including',
  cad: 'computer-aided',
  est: 'existing patient',
  pt: 'patient',
  eval: 'evaluation',
  ther: 'therapy',
  thx: 'therapy',
  inj: 'injection',
  njx: 'injection',
  dx: 'diagnostic',
  tx: 'treatment',
  psytx: 'therapy',
  psych: 'mental health',
  diag: 'evaluation',
  srvcs: 'services',
  'w/': 'with',
  'w/o': 'without',
  'w/med': 'with medicine',
  wo: 'without',
  ct: 'CT',
  us: 'ultrasound',
  abd: 'belly',
  abdo: 'belly',
  comp: 'complete',
  ltd: 'limited',
  xr: 'x-ray',
  xray: 'x-ray',
  dxa: 'bone density scan',
}

/** A HCPCS Level II short name (CMS's) with its abbreviations spelled out: "INJ METHYLPREDNISOLONE 80 MG" → "Injection methylprednisolone 80 mg". */
export function expandShortName(name: string): string {
  const words = name.split(/\s+/).flatMap((word) => {
    const key = word.toLowerCase().replace(/[,.;]+$/, '')
    if (key in ABBREVIATIONS) return ABBREVIATIONS[key] ? [ABBREVIATIONS[key]!] : []
    return [word]
  })
  return sentenceCase(words.join(' ').toUpperCase()).replace(/\bCT\b/i, 'CT')
}

/**
 * The start of a HCPCS Level II long name (CMS's), as a title: up to the first comma, semicolon or parenthesis, at most 8
 * words. "GROUND MILEAGE, PER STATUTE MILE" → "Ground mileage". A one-word start keeps the next part:
 * "INJECTION, ONABOTULINUMTOXINA, 1 UNIT" → "Injection, onabotulinumtoxina".
 */
export function descriptionTitle(description: string): string | null {
  const parts = description.split(/[,;(]/).map((part) => part.trim()).filter(Boolean)
  if (!parts.length) return null
  let title = parts[0]!
  if (title.split(/\s+/).length === 1 && parts[1]) title = `${title}, ${parts[1]}`
  const words = title.split(/\s+/)
  if (words.length > 8) title = words.slice(0, 8).join(' ')
  return sentenceCase(title.toUpperCase())
}

/** A CPT code (the AMA's) or a CDT code (the ADA's): we have no license for their text, so the app never shows it. */
export function isLicensedCode(code: string): boolean {
  return /^(\d{4}[0-9A-Z]|D\d{4})$/i.test(code.trim())
}

/** Our own plain name for a code (codeNames.ts), or null. */
export function ownName(code: string): string | null {
  return CODE_NAMES[code.trim().toUpperCase()] ?? null
}

/**
 * A service's name beside its code ("CPT 45378 · Colonoscopy"): our own name, else the one the API sends (CMS's RBCS name
 * for a CPT code, CMS's or the plan's for a HCPCS Level II code, our label for a revenue code).
 */
export function codeName(code: string, name: string | null | undefined): string | null {
  return ownName(code) ?? (name || null)
}

/**
 * What to call a service: our own plain name ("Colonoscopy"); for a CPT or CDT code without one, CMS's RBCS name and the
 * code ("Lower GI endoscopy (45399)"), never the plan's text; for a HCPCS Level II code, the start of its long name, else
 * its short name with the abbreviations spelled out; for a revenue code, its label ("Ambulatory surgery charge").
 */
export function serviceTitle(code: string, name: string | null | undefined, description?: string | null, revenueLabel?: string | null): string {
  const plain = ownName(code)
  if (plain) return plain
  if (isLicensedCode(code)) return name ? `${sentenceCase(name)} (${code})` : `Code ${code}`
  const fromDescription = description ? descriptionTitle(description) : null
  if (fromDescription) return fromDescription
  if (name) return expandShortName(name)
  if (revenueLabel) return `${revenueLabel} charge`
  return `Code ${code}`
}


/**
 * Codes whose plain name or alias has every word typed, best first (an exact name, then a name that starts with the
 * words, then the rest): "colonoscopy" → 45378 first; "mri" → the MRI codes; "botox" → J0585.
 */
export function plainNameMatches(text: string): { code: string; name: string }[] {
  const typed = text.trim().toLowerCase()
  if (typed.length < 3) return []
  const words = typed.split(/\s+/)
  const entries = [
    ...Object.entries(CODE_NAMES).map(([code, name]) => ({ code, name, keys: name.toLowerCase() })),
    ...Object.entries(ALIASES).map(([alias, code]) => ({ code, name: CODE_NAMES[code] ?? alias, keys: alias })),
  ]
  const scored = entries
    .filter((entry) => words.every((word) => entry.keys.split(/[\s,/]+/).some((key) => key.startsWith(word))))
    .map((entry) => ({ ...entry, score: entry.keys === typed ? 0 : entry.keys.startsWith(typed) ? 1 : 2 }))
    .sort((a, b) => a.score - b.score || a.name.length - b.name.length)
  const seen = new Set<string>()
  return scored.filter((entry) => !seen.has(entry.code) && seen.add(entry.code)).map(({ code, name }) => ({ code, name }))
}

/** True when the text looks like a billing code rather than words: 45378, 0490, J0585, 0001U. */
export function looksLikeCode(text: string): boolean {
  return /^(\d{3,5}|[A-Z]\d{4}|\d{4}[A-Z])$/i.test(text.trim())
}

const SUFFIX = /,?\s+(?:l\.?l\.?c\.?|inc\.?|p\.?c\.?|pllc|llp|l\.?p\.?|corp\.?|co\.?|ltd\.?|p\.?a\.?)$/i

/**
 * A provider's name without legal suffixes or a "doing business as" prefix:
 * "NHPE, LLC dba New Hyde Park Endoscopy" → "New Hyde Park Endoscopy"; "Yorkville Endoscopy, LLC" → "Yorkville Endoscopy".
 * All-capitals names become title case ("MOUNT SINAI HOSPITAL" → "Mount Sinai Hospital").
 */
export function providerName(name: string): string {
  let clean = name.trim()
  const dba = clean.match(/\b(?:d\/?b\/?a|doing business as)\b\s*(.+)$/i)
  if (dba?.[1]) clean = dba[1].trim()
  for (let i = 0; i < 3 && SUFFIX.test(clean); i++) clean = clean.replace(SUFFIX, '').trim()
  clean = clean.replace(/[,\s]+$/, '')
  if (!/[a-z]/.test(clean)) {
    clean = clean
      .toLowerCase()
      .split(' ')
      .map((word, index) => {
        if (KEEP_UPPER.has(word.toUpperCase())) return word.toUpperCase()
        if (index > 0 && SMALL_WORDS.has(word)) return word
        // Acronyms stay as they are: no vowel ("MVHS"), or three letters or fewer ("NYU").
        if (/^[a-z]+$/.test(word) && (!/[aeiouy]/.test(word) || word.length <= 3)) return word.toUpperCase()
        return word.charAt(0).toUpperCase() + word.slice(1)
      })
      .join(' ')
  }
  return clean || name
}

/** A place without the state when it's New York: "Rome, NY (Oneida County)" → "Rome"; "Newark, NJ" stays. */
export function placeName(location: string | null | undefined): string | null {
  if (!location) return null
  const withoutCounty = location.replace(/\s*\([^)]*\)\s*$/, '').trim()
  return withoutCounty.replace(/,\s*NY$/i, '').trim() || null
}

/** "27 places", "1 place". */
export function placesCount(count: number): string {
  return `${count.toLocaleString('en-US')} ${count === 1 ? 'place' : 'places'}`
}
