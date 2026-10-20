import type { RateSearchResult } from '../api/types'
import { filedUnderRevenueCode } from './lenses'

// "How it's priced": a short note for each way the search's prices are paid. Each says only what billing rules and the
// plan's files support, in plain words.

export interface PricingNote {
  key: string
  title: string
  text: string
}

export function pricingNotes(result: RateSearchResult): PricingNote[] {
  const code = result.code
  if (!code) return []
  const notes: PricingNote[] = []
  const groups = result.groups
  const kind = code.category.key

  if (kind === 'anesthesia') {
    notes.push({
      key: 'anesthesia',
      title: 'Anesthesia is paid by the unit',
      text:
        'A case is a number of base units for the procedure plus time units, usually one for each 15 minutes. Each price ' +
        "here is for one unit; how many units a case takes isn't in these files.",
    })
  }

  if (kind === 'drugs') {
    const unit = code.doseUnit ? ` (for ${code.code}, ${code.doseUnit.text})` : ''
    notes.push({
      key: 'drugs',
      title: 'Drugs are priced per HCPCS unit',
      text: `One unit is the amount in the code's name${unit}. A dose of several units is paid several times the price.`,
    })
  }

  if (kind === 'supplies') {
    notes.push({ key: 'supplies', title: 'Supplies are priced per item', text: "Each price is for one item or unit, as the code's name says." })
  }

  if (kind === 'lab') {
    notes.push({ key: 'lab', title: 'Lab tests are priced per test', text: 'Each price is for one test.' })
  }

  if (kind === 'facility') {
    notes.push({
      key: 'revenue-code',
      title: 'A revenue code alone names a part of the hospital, not a service',
      text:
        "Revenue code 0450, for example, is the emergency room. The file doesn't say what the charge covers or in what " +
        "unit, so these prices aren't comparable with a procedure's.",
    })
  } else if (groups.some(filedUnderRevenueCode)) {
    notes.push({
      key: 'facility-rates',
      title: 'Facility rates are filed under revenue codes',
      text:
        "The hospital's or surgery center's charge is filed under a revenue code (such as 0490, ambulatory surgery), with " +
        "this code in a notes field. It's the facility's part only: the doctor's fee is a separate price.",
    })
  }

  if (groups.some((group) => group.settingKey === 'fqhc' || group.settingKey === 'rhc')) {
    notes.push({
      key: 'health-centers',
      title: 'Health centers are paid per visit',
      text:
        "Community health centers (FQHCs) and rural health clinics get one all-inclusive amount per visit, whatever the " +
        "service, so their prices aren't comparable with a doctor's office.",
    })
  }

  if (kind === 'imaging' && groups.some((group) => group.modifiers.some((modifier) => modifier.code === '26' || modifier.code === 'TC'))) {
    notes.push({
      key: 'imaging-split',
      title: 'Imaging is often paid in two parts',
      text:
        "The doctor's reading (modifier 26) and the scan itself, the equipment and staff (modifier TC), can each have a " +
        'price. A price with neither modifier covers both.',
    })
  }

  if (code.levelFamily) {
    notes.push({
      key: 'levels',
      title: 'Visits come in levels',
      text:
        'The level (1 to 5) is set by how complex the visit is, and each level has its own price, so compare level to ' +
        "level. The files don't say how often each level is billed.",
    })
  }

  return notes
}
