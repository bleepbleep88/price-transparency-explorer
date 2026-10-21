import { describe, expect, it } from 'vitest'
import { CODE_NAMES } from '../codeNames'
import {
  billLine,
  billName,
  codeName,
  isLicensedCode,
  looksLikeCode,
  modifierWords,
  placeName,
  placesCount,
  plainNameMatches,
  providerName,
  sentenceCase,
  serviceTitle,
  settingName,
} from '../plainNames'

describe('plain names', () => {
  it('names settings and bills the way people say them', () => {
    expect(settingName('asc')).toBe('Surgery center')
    expect(settingName('hospital-outpatient')).toBe('Hospital')
    expect(settingName('nope', 'Some place')).toBe('Some place')
    expect(billName({ feeType: 'facility', settingKey: 'asc' })).toEqual({ name: 'Surgery center', where: null })
    expect(billName({ feeType: 'professional', settingKey: 'office' })).toEqual({ name: 'Doctor', where: 'in the office' })
    // A health center's visit is paid one price for the whole visit; a lab test there isn't a visit.
    expect(billName({ feeType: 'facility', settingKey: 'fqhc', code: '99213' })).toEqual({ name: 'Health center visit (all in one price)', where: null })
    expect(billName({ feeType: 'facility', settingKey: 'fqhc', code: '86803' })).toEqual({ name: 'Health center', where: null })
    // Physical and occupational therapy is the therapist's bill.
    expect(billName({ feeType: 'professional', settingKey: 'office', code: '97530' })).toEqual({ name: 'Therapist', where: 'in the office' })
    expect(billLine({ feeType: 'professional', settingKey: 'asc', modifiers: [{ code: '33', label: 'Preventive service (modifier 33)' }] })).toBe(
      'Doctor · at a surgery center · preventive',
    )
    expect(modifierWords([{ code: 'LT', label: 'Left side (modifier LT)' }, { code: 'RT', label: 'Right side (modifier RT)' }])).toBe('left, right')
    // A few plain words per modifier, and each said once: 95 and GT are both "video".
    expect(modifierWords([{ code: '25', label: 'x' }, { code: '95', label: 'y' }, { code: 'GT', label: 'z' }])).toBe('same day as a procedure, video')
  })

  it("calls a CPT code by our own plain name, else CMS's RBCS name and the code, never the plan's text", () => {
    expect(serviceTitle('45378', 'Lower GI endoscopy')).toBe('Colonoscopy')
    expect(serviceTitle('93925', 'Duplex scan, extremity arteries')).toBe('Leg artery ultrasound')
    // No name of ours: the RBCS name the API sends, with the code, so it reads as a category, not a name for the service.
    expect(serviceTitle('45399', 'Digestive/Gastrointestinal')).toBe('Digestive/Gastrointestinal (45399)')
    expect(serviceTitle('00731', 'Anesthesia')).toBe('Anesthesia (00731)')
    expect(serviceTitle('0001U', null)).toBe('Code 0001U')
    expect(serviceTitle('0490', null)).toBe('Code 0490')
    expect(codeName('45378', 'Lower GI endoscopy')).toBe('Colonoscopy')
    expect(codeName('45399', 'Digestive/Gastrointestinal')).toBe('Digestive/Gastrointestinal')
    expect(isLicensedCode('45378')).toBe(true)
    expect(isLicensedCode('0042T')).toBe(true)
    expect(isLicensedCode('D0120')).toBe(true)
    expect(isLicensedCode('J0585')).toBe(false)
    expect(isLicensedCode('0490')).toBe(false)
    expect(sentenceCase('GROUND MILEAGE PER MILE')).toBe('Ground mileage per mile')
    expect(sentenceCase('Already Mixed Case')).toBe('Already Mixed Case')
  })

  it('keeps our own names short and plain', () => {
    const tooLong = Object.entries(CODE_NAMES).filter(([, name]) => name.split(/\s+/).length > 8)
    const notACapital = Object.entries(CODE_NAMES).filter(([, name]) => !/^[A-Z0-9]/.test(name))
    // Descriptor shorthand never belongs in our own names.
    const shorthand = Object.entries(CODE_NAMES).filter(([, name]) => /w\/|EST|NOS|\(eg/.test(name))
    expect([...tooLong, ...notACapital, ...shorthand]).toEqual([])
    expect(new Set(Object.values(CODE_NAMES).map((name) => name.toLowerCase())).size).toBe(Object.keys(CODE_NAMES).length)
  })

  it('cleans provider names', () => {
    expect(providerName('NHPE, LLC dba New Hyde Park Endoscopy')).toBe('New Hyde Park Endoscopy')
    expect(providerName('Yorkville Endoscopy, LLC')).toBe('Yorkville Endoscopy')
    expect(providerName('Avicenna ASC, Inc.')).toBe('Avicenna ASC')
    expect(providerName('General Physician, PC')).toBe('General Physician')
    expect(providerName('MOUNT SINAI HOSPITAL')).toBe('Mount Sinai Hospital')
    expect(providerName('UNIVERSITY OF ROCHESTER')).toBe('University of Rochester')
    // Acronyms stay: no vowel, or three letters or fewer.
    expect(providerName('MVHS INC')).toBe('MVHS')
    expect(providerName('NYU LANGONE HOSPITALS')).toBe('NYU Langone Hospitals')
  })

  it("titles a HCPCS code from CMS's long or short name, a revenue code from its label, and finds plain names typed", () => {
    expect(serviceTitle('J9999', null, 'INJECTION, ONABOTULINUMTOXINA, 1 UNIT')).toBe('Injection, onabotulinumtoxina')
    expect(serviceTitle('A0999', null, 'GROUND MILEAGE, PER STATUTE MILE')).toBe('Ground mileage')
    expect(serviceTitle('J1040', 'INJ METHYLPREDNISOLONE 80 MG')).toBe('Injection methylprednisolone 80 mg')
    expect(serviceTitle('0490', null, null, 'Ambulatory surgery')).toBe('Ambulatory surgery charge')
    expect(plainNameMatches('colonoscopy')[0]?.code).toBe('45378')
    expect(plainNameMatches('botox')[0]?.code).toBe('J0585')
    expect(plainNameMatches('knee mri')[0]?.code).toBe('73721')
    expect(plainNameMatches('mri').map((match) => match.code)).toEqual(expect.arrayContaining(['73721', '70553']))
    expect(looksLikeCode('45378')).toBe(true)
    expect(looksLikeCode('J0585')).toBe(true)
    expect(looksLikeCode('botox')).toBe(false)
  })

  it('drops New York from places and counts places', () => {
    expect(placeName('Rome, NY (Oneida County)')).toBe('Rome')
    expect(placeName('Manhattan, NY')).toBe('Manhattan')
    expect(placeName('Newark, NJ')).toBe('Newark, NJ')
    expect(placeName(null)).toBeNull()
    expect(placesCount(1)).toBe('1 place')
    expect(placesCount(1200)).toBe('1,200 places')
  })
})
