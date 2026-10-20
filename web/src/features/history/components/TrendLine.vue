<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import type { GroupTrend } from '../api/types'
import { drawableSeries, hasHistory, medianTrendText, partialNote, providerTrendText } from '../lib/trend'

const TrendChart = defineAsyncComponent(() => import('./TrendChart.vue'))

// A group card's history across the plan's monthly files: a small chart and the same numbers in words. Groups too small
// for a median show nothing unless the searched provider has a rate to follow.
const props = defineProps<{
  trend: GroupTrend
  /** The snapshot on screen, marked on the chart. */
  selectedId: number | null
  /** An NPI or EIN picked out one provider: follow its rate too. */
  showProvider: boolean
}>()

const median = computed(() => medianTrendText(props.trend))
const provider = computed(() => (props.showProvider ? providerTrendText(props.trend) : null))
const partial = computed(() => partialNote(props.trend))
const drawable = computed(() => drawableSeries(props.trend, props.showProvider))
</script>

<template>
  <div v-if="hasHistory(trend) && (median || provider)" class="d-flex flex-column ga-1" data-testid="trend-line">
    <p class="text-body-small font-weight-medium">Across the plan's monthly files</p>
    <TrendChart v-if="drawable" :trend="trend" :selected-id="selectedId" :show-provider="showProvider" />
    <p v-if="median" class="text-body-small text-medium-emphasis" data-testid="trend-median">{{ median }}</p>
    <p v-if="provider" class="text-body-small" data-testid="trend-provider">{{ provider }}</p>
    <p v-if="partial" class="text-body-small text-medium-emphasis">{{ partial }}</p>
  </div>
  <p v-else-if="!hasHistory(trend)" class="text-body-small text-medium-emphasis" data-testid="trend-only-here">
    No other loaded file has this comparison group.
  </p>
</template>
