import { wholeNumber } from '@/lib/format'
import type { LocationOption } from '../api/types'

// New York counties and NYC boroughs from the provider directory. The API lists the boroughs first, then the counties
// with the most NPIs.

/** Finds a location by its label, ignoring case and spaces. */
export function findLocation(label: string | null | undefined, locations: readonly LocationOption[]): LocationOption | null {
  const wanted = label?.trim().toLowerCase()
  if (!wanted) return null
  return locations.find((location) => location.label.toLowerCase() === wanted) ?? null
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
}

/** Picker items: the five boroughs under their own heading, then every other county. */
export function locationSelectItems(locations: readonly LocationOption[]): LocationSelectItem[] {
  const toItem = (location: LocationOption): LocationSelectItem => ({
    type: 'item',
    title: location.isBorough ? location.label : `${location.label} County`,
    value: location.label,
    subtitle: `${wholeNumber(location.npis)} NPIs`,
  })
  const boroughs = locations.filter((location) => location.isBorough)
  const counties = locations.filter((location) => !location.isBorough)
  return [
    ...(boroughs.length ? [{ type: 'subheader' as const, title: 'New York City boroughs' }, ...boroughs.map(toItem)] : []),
    ...(counties.length ? [{ type: 'subheader' as const, title: 'Other New York counties' }, ...counties.map(toItem)] : []),
  ]
}
