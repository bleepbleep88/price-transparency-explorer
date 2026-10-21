<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { providerName } from '@/lib/plainNames'
import type { GroupTrend } from '../api/types'
import { drawableSeries, followedProvider, hasHistory, partialNote, providerByMonth, typicalByMonth, whoLine } from '../lib/trend'

const TrendChart = defineAsyncComponent(() => import('./TrendChart.vue'))

// A group's history across the plan's monthly files, for Details (decision 90): the chart, the prices by month, and who
// stayed or left for the last move. The tile's chip (trendChip.ts) is the short answer.
const props = defineProps<{
  trend: GroupTrend
  /** The month on screen, marked on the chart. */
  selectedId: number | null
  /** An NPI or EIN picked out one provider: follow its rate too. */
  showProvider: boolean
}>()

const typical = computed(() => typicalByMonth(props.trend))
const who = computed(() => whoLine(props.trend, props.selectedId))
const provider = computed(() => {
  const name = props.showProvider ? followedProvider(props.trend, props.selectedId) : null
  const rates = name ? providerByMonth(props.trend) : null
  return name && rates ? `${providerName(name)}: ${rates}` : null
})
const partial = computed(() => partialNote(props.trend))
const drawable = computed(() => drawableSeries(props.trend, props.showProvider))
</script>

<template>
  <div v-if="hasHistory(trend) && (typical || provider)" class="d-flex flex-column ga-1" data-testid="trend-line">
    <p class="trend-label">By month</p>
    <TrendChart v-if="drawable" :trend="trend" :selected-id="selectedId" :show-provider="showProvider" />
    <p v-if="typical" data-testid="trend-typical">Typical: {{ typical }}</p>
    <p v-if="who" class="text-medium-emphasis" data-testid="trend-who">{{ who }}</p>
    <p v-if="provider" data-testid="trend-provider">{{ provider }}</p>
    <p v-if="partial" class="text-medium-emphasis">{{ partial }}</p>
  </div>
  <p v-else-if="!hasHistory(trend)" class="text-medium-emphasis" data-testid="trend-only-here">No other month has these prices.</p>
</template>

<style scoped>
.trend-label {
  font-weight: 600;
}
</style>
