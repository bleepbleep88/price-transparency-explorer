<script setup lang="ts">
import { computed } from 'vue'
import { monthDay } from '@/lib/format'
import type { RateSearchResult } from '@/features/rates/api/types'
import { lowerFirst } from '@/lib/plainNumbers'
import { costLines, pinsProvider } from '@/features/rates/lib/lenses'
import type { Lens, SearchParams } from '@/features/rates/lib/searchParams'
import type { RateTrendResult } from '../api/types'
import { useChanges } from '../composables/useHistoryQueries'
import { useSnapshotChoice } from '../composables/useSnapshotChoice'
import { DEFAULT_CHANGE_SORT } from '../lib/changesParams'
import { previousSnapshot } from '../lib/snapshots'
import { changesSinceText } from '../lib/changesText'
import { hasHistory, providerTrendText, trendFor, typicalTrendText } from '../lib/trend'

// One line of history per lens: providers follow their own rate, plans see what changed since the last file, employers
// follow the typical price. Shown only when more than one snapshot is loaded.
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
    const text = providerTrendText(trend)
    if (text) return { groupKey: group.key, text }
  }
  return null
})

/** Employers: the largest group with a typical price, as the cost table above lists first. */
const typical = computed(() => {
  if (props.lens !== 'employers') return null
  for (const line of costLines(props.result)) {
    const trend = trendFor(props.trend, line.groupKey)
    const text = hasHistory(trend) ? typicalTrendText(trend) : null
    if (text) return { groupKey: line.groupKey, text: `For the setting and plan with the most providers, ${lowerFirst(text)}` }
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
    sort: DEFAULT_CHANGE_SORT,
    page: 1,
    pageSize: 1,
  }),
  () => props.lens === 'plans' && previous.value != null,
)
// Counts from saved statistics that are out of date (or not built yet) aren't shown as current.
const changesText = computed(() => {
  const data = changes.data.value
  if (!data || !previous.value) return null
  const since = monthDay(previous.value.indexDate)
  return data.current ? changesSinceText(data.counts, since, code.value) : `The changes since the ${since} file are being updated.`
})
const line = computed(() => yours.value ?? typical.value)
const changesLink = computed(() => ({
  name: 'changes',
  query: { ...carried.value, code: code.value, ...(props.params.product ? { product: props.params.product } : {}) },
}))
</script>

<template>
  <template v-if="line || changesText || (lens === 'plans' && changes.isFetching.value)">
    <v-divider />
    <div v-if="line" class="d-flex flex-wrap align-center ga-2" data-testid="lens-history">
      <p class="text-body-medium">{{ line.text }}</p>
      <v-btn variant="text" size="small" color="primary" data-testid="show-trend" @click="emit('showTrend', line.groupKey)">
        See the trend
      </v-btn>
    </div>
    <div v-else-if="changesText" class="d-flex flex-wrap align-center ga-2" data-testid="lens-history">
      <p class="text-body-medium">{{ changesText }}</p>
      <v-btn variant="text" size="small" color="primary" :to="changesLink" data-testid="see-changes">See the changes</v-btn>
    </div>
    <p v-else class="text-body-small text-medium-emphasis">Comparing with the last file…</p>
  </template>
</template>
