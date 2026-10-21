// Plain names for the main view (decision 90): what a five-year-old would call a place, a bill or a provider.
// The API keeps the precise labels; Details panels can still show them.

import type { ModifierRef } from '@/features/rates/api/types'

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

/**
 * The bill a price is for, as a tile names it. A facility fee is the place's bill ("Surgery center"); a professional
 * fee is the doctor's ("Doctor", with where in `where`).
 */
export function billName(group: { feeType: string; settingKey: string; settingLabel?: string }): { name: string; where: string | null } {
  if (group.feeType === 'professional') return { name: 'Doctor', where: DOCTOR_WHERE[group.settingKey] ?? null }
  return { name: settingName(group.settingKey, group.settingLabel), where: null }
}

/** One short line for a bill in a list: "Doctor · in the office", "Surgery center". */
export function billLine(group: { feeType: string; settingKey: string; settingLabel?: string; modifiers?: ModifierRef[] }): string {
  const { name, where } = billName(group)
  const extra = modifierWords(group.modifiers ?? [])
  return [name, where, extra].filter(Boolean).join(' · ')
}

/** Modifiers in a few lower-case words, no codes: "preventive", "left side, right side". Empty without any. */
export function modifierWords(modifiers: ModifierRef[]): string {
  return modifiers
    .map((modifier) => modifier.label.replace(/\s*\(modifier [^)]*\)\s*/i, '').trim())
    .filter(Boolean)
    .map((label) => label.charAt(0).toLowerCase() + label.slice(1))
    .join(', ')
}

const COMMON_NAMES: Record<string, string> = {
  '45378': 'Colonoscopy',
  '45380': 'Colonoscopy with biopsy',
  '45385': 'Colonoscopy, polyp removal',
  '66984': 'Cataract surgery',
  '66982': 'Cataract surgery, complex',
  '29881': 'Knee scope',
  '73721': 'Knee MRI',
  '70450': 'Head CT',
  '70553': 'Brain MRI',
  '74177': 'Belly CT',
  '76856': 'Pelvic ultrasound',
  '99202': 'Office visit, new patient, level 2',
  '99203': 'Office visit, new patient, level 3',
  '99204': 'Office visit, new patient, level 4',
  '99205': 'Office visit, new patient, level 5',
  '99211': 'Office visit, level 1',
  '99212': 'Office visit, level 2',
  '99213': 'Office visit, level 3',
  '99214': 'Office visit, level 4',
  '99215': 'Office visit, level 5',
  '99281': 'ER visit, level 1',
  '99282': 'ER visit, level 2',
  '99283': 'ER visit, level 3',
  '99284': 'ER visit, level 4',
  '99285': 'ER visit, level 5',
  '97110': 'Physical therapy exercises',
  '90837': 'Therapy session, 1 hour',
  '90834': 'Therapy session, 45 minutes',
  '80053': 'Blood test panel',
  '85025': 'Blood count',
  '00812': 'Colonoscopy anesthesia',
  J0585: 'Botox',
  J1100: 'Dexamethasone',
  J3301: 'Kenalog shot',
  G0463: 'Hospital clinic visit',
}

const SMALL_WORDS = new Set(['a', 'an', 'and', 'at', 'by', 'for', 'in', 'of', 'on', 'or', 'the', 'to', 'with', 'w/', 'w/o'])
const KEEP_UPPER = new Set(['CT', 'MRI', 'PET', 'ER', 'IV', 'GI', 'US', 'EKG', 'ECG', 'HIV', 'COVID', 'DNA', 'RNA', 'ASC', 'NY', 'NYC'])

/** "DIAGNOSTIC COLONOSCOPY" → "Diagnostic colonoscopy"; a name with lower-case letters is kept as published. */
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

/** What to call a service: a plain name for common codes ("Colonoscopy"), else the plan's name in sentence case. */
export function serviceTitle(code: string, name: string | null | undefined): string {
  const plain = COMMON_NAMES[code]
  if (plain) return plain
  return name ? sentenceCase(name) : `Code ${code}`
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
