import { describe, expect, it } from 'vitest'
import { billLine, billName, modifierWords, placeName, placesCount, providerName, sentenceCase, serviceTitle, settingName } from '../plainNames'

describe('plain names', () => {
  it('names settings and bills the way people say them', () => {
    expect(settingName('asc')).toBe('Surgery center')
    expect(settingName('hospital-outpatient')).toBe('Hospital')
    expect(settingName('nope', 'Some place')).toBe('Some place')
    expect(billName({ feeType: 'facility', settingKey: 'asc' })).toEqual({ name: 'Surgery center', where: null })
    expect(billName({ feeType: 'professional', settingKey: 'office' })).toEqual({ name: 'Doctor', where: 'in the office' })
    expect(billLine({ feeType: 'professional', settingKey: 'asc', modifiers: [{ code: '33', label: 'Preventive service (modifier 33)' }] })).toBe(
      'Doctor · at a surgery center · preventive service',
    )
    expect(modifierWords([{ code: 'LT', label: 'Left side (modifier LT)' }, { code: 'RT', label: 'Right side (modifier RT)' }])).toBe('left side, right side')
  })

  it('calls common services by plain names, else the plan name in sentence case', () => {
    expect(serviceTitle('45378', 'DIAGNOSTIC COLONOSCOPY')).toBe('Colonoscopy')
    expect(serviceTitle('93925', 'LOWER EXTREMITY STUDY')).toBe('Lower extremity study')
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
