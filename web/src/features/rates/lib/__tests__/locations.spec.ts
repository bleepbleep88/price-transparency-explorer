import { describe, expect, it } from 'vitest'
import type { LocationOption } from '../../api/types'
import { findLocation, locationSelectItems, locationTitle } from '../locations'

// Shaped like GET /api/v1/reference: boroughs first, then counties by NPI count.
const locations: LocationOption[] = [
  { label: 'Manhattan', isBorough: true, npis: 11845 },
  { label: 'Brooklyn', isBorough: true, npis: 4874 },
  { label: 'Nassau', isBorough: false, npis: 5454 },
  { label: 'Monroe', isBorough: false, npis: 2437 },
]

describe('locations', () => {
  it('finds a location by label, ignoring case and spaces', () => {
    expect(findLocation(' manhattan ', locations)).toEqual(locations[0])
    expect(findLocation('Atlantis', locations)).toBeNull()
    expect(findLocation(undefined, locations)).toBeNull()
  })

  it('names boroughs plainly and counties as counties', () => {
    expect(locationTitle('Manhattan', locations)).toBe('Manhattan')
    expect(locationTitle('monroe', locations)).toBe('Monroe County')
    expect(locationTitle('Atlantis', locations)).toBe('Atlantis')
  })

  it('groups the boroughs first under their own heading', () => {
    const items = locationSelectItems(locations)
    expect(items.map((item) => (item.type === 'subheader' ? `# ${item.title}` : item.title))).toEqual([
      '# New York City boroughs',
      'Manhattan',
      'Brooklyn',
      '# Other New York counties',
      'Nassau County',
      'Monroe County',
    ])
    expect(items[1]).toMatchObject({ value: 'Manhattan', subtitle: '11,845 NPIs' })
    expect(items[4]).toMatchObject({ value: 'Nassau' })
  })

  it('leaves out a heading with nothing under it', () => {
    expect(locationSelectItems(locations.slice(2)).filter((item) => item.type === 'subheader')).toHaveLength(1)
    expect(locationSelectItems([])).toEqual([])
  })
})
