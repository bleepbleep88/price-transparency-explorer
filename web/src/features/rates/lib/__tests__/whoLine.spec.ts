import { describe, expect, it } from 'vitest'
import type { ProviderProfile } from '../../api/types'
import { directorySourceNote, moreCount, moreLines, registryNote, whoLine } from '../whoLine'

// Shaped like a real 45378 row: Gastrointestinal Care of Long Island PLLC (35 NPIs in 11 places; the API lists 3).
function makeProfile(overrides: Partial<ProviderProfile> = {}): ProviderProfile {
  return {
    npis: 35,
    npisInDirectory: 35,
    individuals: 35,
    deactivated: 0,
    specialties: [
      { specialty: 'Gastroenterology Physician', group: 'Allopathic & Osteopathic Physicians', npis: 31 },
      { specialty: 'Family Nurse Practitioner', group: 'Physician Assistants & Advanced Practice Nursing Providers', npis: 3 },
      { specialty: 'Internal Medicine Physician', group: 'Allopathic & Osteopathic Physicians', npis: 1 },
    ],
    locations: ['Brooklyn, NY', 'Bronx, NY', 'New City, NY · Rockland County'],
    locationCount: 11,
    ...overrides,
  }
}

describe('whoLine', () => {
  it('leads with the first specialty and place the API lists, and keeps the rest for "+N more"', () => {
    const who = whoLine(makeProfile())!
    expect(who.specialty).toBe('Gastroenterology Physician')
    expect(who.location).toBe('Brooklyn, NY')
    expect(who.otherSpecialties).toEqual(['Family Nurse Practitioner (3 NPIs)', 'Internal Medicine Physician (1 NPI)'])
    expect(who.otherLocations).toEqual(['Bronx, NY', 'New City, NY · Rockland County'])
    expect(who.unlistedPlaces).toBe(8)
    expect(moreCount(who)).toBe(12)
    expect(who.registryNote).toBeNull()
  })

  it('shows the place the API puts first for a location filter, with its county outside New York City', () => {
    const who = whoLine(makeProfile({ locations: ['Rome, NY · Oneida County', 'Utica, NY · Oneida County'], locationCount: 2 }))!
    expect(who.location).toBe('Rome, NY · Oneida County')
    expect(who.otherLocations).toEqual(['Utica, NY · Oneida County'])
    expect(who.unlistedPlaces).toBe(0)
  })

  it('copes with an empty profile and with no profile at all', () => {
    expect(whoLine(null)).toBeNull()
    const who = whoLine(makeProfile({ specialties: [], locations: [], locationCount: 0 }))!
    expect(who.specialty).toBeNull()
    expect(who.location).toBeNull()
    expect(moreCount(who)).toBe(0)
  })
})

describe('registryNote', () => {
  it.each([
    [{ npis: 5, npisInDirectory: 5 }, null],
    [{ npis: 5, npisInDirectory: 3 }, '3 of 5 NPIs found in the registry'],
    [{ npis: 4, npisInDirectory: 0 }, 'None of the 4 NPIs found in the registry'],
    [{ npis: 1, npisInDirectory: 0 }, 'NPI not found in the registry'],
  ])('%o -> %s', (counts, note) => {
    expect(registryNote(makeProfile(counts))).toBe(note)
  })
})

describe('moreLines', () => {
  it('lists other specialties and places in plain words, counting places the API left out', () => {
    expect(moreLines(whoLine(makeProfile())!)).toEqual([
      'Also: Family Nurse Practitioner (3 NPIs); Internal Medicine Physician (1 NPI)',
      'Other places: Bronx, NY; New City, NY · Rockland County; and 8 more places',
    ])
  })

  it('caps a long specialty list', () => {
    const specialties = Array.from({ length: 12 }, (_, i) => ({ specialty: `Specialty ${i + 1}`, group: null, npis: 12 - i }))
    const lines = moreLines(whoLine(makeProfile({ specialties, locations: [], locationCount: 0 }))!, 3)
    expect(lines).toEqual(['Also: Specialty 2 (11 NPIs); Specialty 3 (10 NPIs); Specialty 4 (9 NPIs); and 8 more specialties'])
  })
})

describe('directorySourceNote', () => {
  it('cites the release the API reports, and just the registry when it is unknown', () => {
    expect(directorySourceNote({ release: 'October 2026', builtAt: null, npisLoaded: 58688, source: null })).toBe(
      'Specialty and location from the national provider registry (NPPES, October 2026).',
    )
    expect(directorySourceNote(null)).toBe('Specialty and location from the national provider registry (NPPES).')
  })
})
