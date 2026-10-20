import { describe, expect, it } from 'vitest'
import type { LocationOption } from '../../api/types'
import { findLocation, locationSelectItems, locationTitle } from '../locations'

// Shaped like GET /api/v1/reference: boroughs first, then counties by NPI count.
const locations: LocationOption[] = [
  { label: 'Manhattan', isBorough: true, npis: 11845, county: 'New York County' },
  { label: 'Brooklyn', isBorough: true, npis: 4874, county: 'Kings County' },
  { label: 'Nassau', isBorough: false, npis: 5454, county: 'Nassau County' },
  { label: 'Monroe', isBorough: false, npis: 2437, county: 'Monroe County' },
]

describe('locations', () => {
  it('finds a location by label, ignoring case and spaces', () => {
    expect(findLocation(' manhattan ', locations)).toEqual(locations[0])
    expect(findLocation('Atlantis', locations)).toBeNull()
    expect(findLocation(undefined, locations)).toBeNull()
  })

  it("finds a borough by its county's name, and a county with or without the word", () => {
    expect(findLocation('Kings', locations)).toEqual(locations[1])
    expect(findLocation('kings county', locations)).toEqual(locations[1])
    expect(findLocation('New York', locations)).toEqual(locations[0])
    expect(findLocation('Monroe County', locations)).toEqual(locations[3])
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
    // A borough shows its county's name, which the picker also matches on ("Kings" finds Brooklyn).
    expect(items[1]).toMatchObject({ value: 'Manhattan', subtitle: 'New York County · 11,845 NPIs', county: 'New York County' })
    expect(items[2]).toMatchObject({ value: 'Brooklyn', subtitle: 'Kings County · 4,874 NPIs', county: 'Kings County' })
    expect(items[4]).toMatchObject({ value: 'Nassau', subtitle: '5,454 NPIs', county: 'Nassau County' })
  })

  it('counts one NPI as one', () => {
    const [, item] = locationSelectItems([{ label: 'Hamilton', isBorough: false, npis: 1, county: 'Hamilton County' }])
    expect(item).toMatchObject({ value: 'Hamilton', title: 'Hamilton County', subtitle: '1 NPI' })
  })

  it('leaves out a heading with nothing under it', () => {
    expect(locationSelectItems(locations.slice(2)).filter((item) => item.type === 'subheader')).toHaveLength(1)
    expect(locationSelectItems([])).toEqual([])
  })
})
