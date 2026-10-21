<script setup lang="ts">
import { computed } from 'vue'
import { monthDay } from '@/lib/format'
import type { RateSearchResult } from '@/features/rates/api/types'
import { lowerFirst } from '@/lib/plainNumbers'
import { billName } from '@/lib/plainNames'
import { costLines, pinsProvider } from '@/features/rates/lib/lenses'
import type { Lens, SearchParams } from '@/features/rates/lib/searchParams'
import type { RateTrendResult } from '../api/types'
import { useChanges } from '../composables/useHistoryQueries'
import { useSnapshotChoice } from '../composables/useSnapshotChoice'
import { DEFAULT_CHANGE_SORT } from '../lib/changesParams'
import { previousSnapshot } from '../lib/snapshots'
import { countsLine, WAITING_SHORT } from '../lib/changesText'
import { hasHistory, trendFor } from '../lib/trend'
import { providerChipText, trendChipText } from '../lib/trendChip'

// One short line of history per lens (decision 90): providers follow their own rate ("Your rate: up $17.41 since Jul"),
// plans see the counts since the month before ("Since Aug 31: 56 up · 57 down"), employers the biggest bill's chip
// ("Surgery center: rates steady since Jul"). Shown only when more than one month is loaded.
const props = defineProps<{ lens: Lens; result: RateSearchResult; params: SearchParams; trend: RateTrendResult | null }>()
const emit = defineEmits<{ showTrend: [groupKey: string] }>()

const { snapshots, current, carried } = useSnapshotChoice()
const previous = computed(() => previousSnapshot(snapshots.value, current.value))

/** Providers: the first card where the searched provider has a rate and the group has a history. */
const yours = computed(() => {
  if (props.lens !== 'providers' || !pinsProvider(props.result)) return null
  for (const group of props.result.groups) {
    const trend = trendFor(props.trend, group.key)
    if (!group.highlights.length || !hasHistory(trend)) continue
    // The providers lens speaks to the provider, so here it is "your rate".
    const chip = providerChipText(trend, props.trend?.selected.snapshotId ?? null)
    if (chip) return { groupKey: group.key, text: `Your rate: ${lowerFirst(chip)}` }
  }
  return null
})

/** Employers: the bill with the most places and a typical price, as the cost reading lists first. */
const typical = computed(() => {
  if (props.lens !== 'employers') return null
  for (const line of costLines(props.result)) {
    const trend = trendFor(props.trend, line.groupKey)
    const chip = hasHistory(trend) ? trendChipText(trend, props.trend?.selected.snapshotId ?? null) : null
    const group = props.result.groups.find((candidate) => candidate.key === line.groupKey)
    if (chip && group) return { groupKey: line.groupKey, text: `${billName(group).name}: ${lowerFirst(chip)}` }
  }
  return null
})

/** Plans: this code's changes since the file before the one on screen. */
const code = computed(() => (props.result.query.codeType ? `${props.result.query.codeType} ${props.result.query.code}` : props.result.query.code))
const changes = useChanges(
  () => ({
    snapshot: current.value?.isLatest ? undefined : current.value?.snapshotId,
    code: code.value,
    product: props.params.product,
    hideListedElsewhere: false,
    // The searched code's own changes: a revenue code's facility charges are what it was searched for, and nothing is ranked.
    includeFacilityCharges: true,
    sort: DEFAULT_CHANGE_SORT,
    page: 1,
    pageSize: 1,
  }),
  () => props.lens === 'plans' && previous.value != null,
)
// Counts come from numbers saved after loading. As on the changes page, there are none until both files' numbers are
// saved and current (the API sends current: false and no counts); the line refreshes when loading finishes.
const changesText = computed(() => {
  const data = changes.isError.value ? undefined : changes.data.value
  if (!data || !previous.value) return null
  return data.current && data.counts ? `Since ${monthDay(previous.value.indexDate)}: ${countsLine(data.counts)}` : WAITING_SHORT
})
const line = computed(() => yours.value ?? typical.value)
const changesLink = computed(() => ({
  name: 'changes',
  query: {
    ...carried.value,
    code: code.value,
    includeFacilityCharges: 'true',
    ...(props.params.product ? { product: props.params.product } : {}),
  },
}))
</script>

<template>
  <template v-if="line || changesText || (lens === 'plans' && changes.isFetching.value)">
    <div v-if="line" class="d-flex flex-wrap align-center ga-2" data-testid="lens-history">
      <p>{{ line.text }}</p>
      <v-btn variant="text" size="small" color="primary" data-testid="show-trend" @click="emit('showTrend', line.groupKey)">
        See the trend
      </v-btn>
    </div>
    <div v-else-if="changesText" class="d-flex flex-wrap align-center ga-2" data-testid="lens-history">
      <p>{{ changesText }}</p>
      <v-btn variant="text" size="small" color="primary" :to="changesLink" data-testid="see-changes">See changes</v-btn>
    </div>
    <p v-else class="text-medium-emphasis">Checking last month…</p>
  </template>
</template>
