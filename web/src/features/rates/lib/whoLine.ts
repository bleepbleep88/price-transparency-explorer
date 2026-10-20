import { plural } from '@/lib/format'
import type { DirectorySource, ProviderProfile } from '../api/types'

/** Where specialties and places come from, citing the registry release the API reports. */
export function directorySourceNote(source: DirectorySource | null | undefined): string {
  const release = source?.release ? `NPPES, ${source.release}` : 'NPPES'
  return `Specialty and location from the national provider registry (${release}).`
}

/** The compact "who is behind this rate" line under a provider's name. */
export interface WhoLine {
  /** The API lists a specialty matching the specialty filter first, else the one with the most NPIs. */
  specialty: string | null
  /** The API lists a place in the filtered location first ("Rome, NY · Oneida County"), else the one with the most NPIs. */
  location: string | null
  /** Everything else, for the "+N more" tooltip. */
  otherSpecialties: string[]
  otherLocations: string[]
  /** Places the API counted but didn't list (it lists up to 3). */
  unlistedPlaces: number
  /** Set when some NPIs aren't in the registry, e.g. "3 of 5 NPIs found in the registry". */
  registryNote: string | null
}

export function whoLine(profile: ProviderProfile | null | undefined): WhoLine | null {
  if (!profile) return null
  const [first, ...others] = profile.specialties
  return {
    specialty: first?.specialty ?? null,
    location: profile.locations[0] ?? null,
    otherSpecialties: others.map((s) => `${s.specialty} (${plural(s.npis, 'NPI')})`),
    otherLocations: profile.locations.slice(1),
    unlistedPlaces: Math.max(0, profile.locationCount - profile.locations.length),
    registryNote: registryNote(profile),
  }
}

export function registryNote(profile: ProviderProfile): string | null {
  const { npis, npisInDirectory: found } = profile
  if (found >= npis) return null
  if (found === 0) return npis === 1 ? 'NPI not found in the registry' : `None of the ${npis} NPIs found in the registry`
  return `${found} of ${npis} NPIs found in the registry`
}

export function moreCount(who: WhoLine): number {
  return who.otherSpecialties.length + who.otherLocations.length + who.unlistedPlaces
}

/** Lines for the "+N more" tooltip, capped so a 750-NPI department doesn't fill the screen. */
export function moreLines(who: WhoLine, maxSpecialties = 8): string[] {
  const lines: string[] = []
  if (who.otherSpecialties.length) {
    const listed = who.otherSpecialties.slice(0, maxSpecialties)
    const hidden = who.otherSpecialties.length - listed.length
    const rest = hidden > 0 ? `; and ${plural(hidden, 'more specialty', 'more specialties')}` : ''
    lines.push(`Also: ${listed.join('; ')}${rest}`)
  }
  if (who.otherLocations.length || who.unlistedPlaces) {
    const rest = who.unlistedPlaces ? `${who.otherLocations.length ? '; and ' : ''}${plural(who.unlistedPlaces, 'more place')}` : ''
    lines.push(`Other places: ${who.otherLocations.join('; ')}${rest}`)
  }
  return lines
}
