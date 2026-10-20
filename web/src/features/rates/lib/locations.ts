import { plural } from '@/lib/format'
import type { LocationOption } from '../api/types'

// New York counties and NYC boroughs from the provider directory. The API lists the boroughs first, then the counties
// with the most NPIs.

const withoutCounty = (text: string) => text.trim().toLowerCase().replace(/\s+county$/, '')

/**
 * Finds a location by its label or its county's name, ignoring case, spaces and a trailing "County":
 * "manhattan", "Monroe County", and "Kings" or "Kings County" for Brooklyn.
 */
export function findLocation(label: string | null | undefined, locations: readonly LocationOption[]): LocationOption | null {
  const wanted = label ? withoutCounty(label) : ''
  if (!wanted) return null
  return (
    locations.find((location) => location.label.toLowerCase() === wanted) ??
    locations.find((location) => withoutCounty(location.county) === wanted) ??
    null
  )
}

/** "Manhattan" for a borough, "Monroe County" for a county, or the label as given when it isn't in the list. */
export function locationTitle(label: string, locations: readonly LocationOption[]): string {
  const location = findLocation(label, locations)
  if (!location) return label
  return location.isBorough ? location.label : `${location.label} County`
}

export interface LocationSelectItem {
  type: 'subheader' | 'item'
  title: string
  value?: string
  subtitle?: string
  /** The county's own name, so typing "Kings" finds Brooklyn. */
  county?: string
}

/** Picker items: the five boroughs under their own heading, then every other county. */
export function locationSelectItems(locations: readonly LocationOption[]): LocationSelectItem[] {
  // A borough shows its county's name, so a match on "Kings" makes sense.
  const toItem = (location: LocationOption): LocationSelectItem => ({
    type: 'item',
    title: location.isBorough ? location.label : `${location.label} County`,
    value: location.label,
    subtitle: location.isBorough ? `${location.county} · ${plural(location.npis, 'NPI')}` : plural(location.npis, 'NPI'),
    county: location.county,
  })
  const boroughs = locations.filter((location) => location.isBorough)
  const counties = locations.filter((location) => !location.isBorough)
  return [
    ...(boroughs.length ? [{ type: 'subheader' as const, title: 'New York City boroughs' }, ...boroughs.map(toItem)] : []),
    ...(counties.length ? [{ type: 'subheader' as const, title: 'Other New York counties' }, ...counties.map(toItem)] : []),
  ]
}
