import { describe, expect, it } from 'vitest'
import {
  billLine,
  billName,
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

  it('calls common services by plain names, else the plan name in sentence case', () => {
    expect(serviceTitle('45378', 'DIAGNOSTIC COLONOSCOPY')).toBe('Colonoscopy')
    expect(serviceTitle('93971', 'EXTREMITY STUDY')).toBe('Extremity study')
    expect(serviceTitle('93925', 'LOWER EXTREMITY STUDY')).toBe('Leg artery ultrasound')
    expect(serviceTitle('0490', null)).toBe('Code 0490')
    expect(sentenceCase('CT HEAD/BRAIN W/O DYE')).toBe('CT head/brain w/o dye')
    expect(sentenceCase('Already Mixed Case')).toBe('Already Mixed Case')
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

  it('titles a service from its long description or spelled-out short name, and finds plain names typed', () => {
    expect(serviceTitle('00731', 'ANES UPR GI NDSC PX NOS', 'ANESTHESIA FOR UPPER GASTROINTESTINAL ENDOSCOPIC PROCEDURES, ENDOSCOPE INTRODUCED')).toBe(
      'Anesthesia for upper gastrointestinal endoscopic procedures',
    )
    expect(serviceTitle('77067', 'SCR MAMMO BI INCL CAD', 'SCREENING MAMMOGRAPHY, BILATERAL (2-VIEW STUDY)')).toBe('Screening mammography')
    expect(serviceTitle('J9999', null, 'INJECTION, ONABOTULINUMTOXINA, 1 UNIT')).toBe('Injection, onabotulinumtoxina')
    expect(serviceTitle('00731', 'ANES UPR GI NDSC PX NOS')).toBe('Anesthesia upper GI endoscopy procedure')
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
