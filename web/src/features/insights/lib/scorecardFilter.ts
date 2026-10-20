import type { ScorecardGroup } from '../api/types'
import { serviceName } from './insightText'
import { FACILITY_CHARGE_MARK, groupLine } from './plainWords'

// The scorecard's "Filter by code, name, plan or setting" box. Vuetify's table search only looks at its own column
// values (the code, the raw group key), so the page filters the rows itself, on what each row shows.

type FilterableGroup = Pick<
  ScorecardGroup,
  'codeType' | 'code' | 'codeName' | 'categoryLabel' | 'facilityCharge' | 'productLabel' | 'feeType' | 'settingLabel' | 'modifiers' | 'unit'
>

/** What a row says, as the filter reads it: service, category, plan, fee, setting, modifiers and unit. */
export function scorecardRowText(group: FilterableGroup): string {
  return [
    serviceName(group),
    group.categoryLabel,
    group.facilityCharge ? FACILITY_CHARGE_MARK : '',
    groupLine(group),
    group.unit,
  ].join(' ')
}

/**
 * Every word typed has to appear somewhere in the row, in any order and any case: "ambetter emergency" finds
 * Ambetter's ER rows, "office visit" finds "OFFICE/OUTPATIENT VISIT EST". An empty filter keeps every row.
 */
export function filterScorecardGroups<T extends FilterableGroup>(groups: T[], query: string | null | undefined): T[] {
  const words = (query ?? '').toLowerCase().split(/\s+/).filter(Boolean)
  if (!words.length) return groups
  return groups.filter((group) => {
    const text = scorecardRowText(group).toLowerCase()
    return words.every((word) => text.includes(word))
  })
}
