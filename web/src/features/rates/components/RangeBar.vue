<script setup lang="ts">
import { computed } from 'vue'
import { money } from '@/lib/format'
import { dollars, MIN_PROVIDERS_FOR_RANGE, rangeLine, typicalPrice, type PriceSummary } from '@/lib/plainNumbers'

const props = defineProps<{ summary: PriceSummary; marker?: number | null }>()

const span = computed(() => Math.max(props.summary.max - props.summary.min, 0.01))
const position = (value: number) => `${(((value - props.summary.min) / span.value) * 100).toFixed(2)}%`
const width = (from: number, to: number) => `${(((to - from) / span.value) * 100).toFixed(2)}%`

const typical = computed(() => typicalPrice(props.summary))
// The shaded band is "most providers" (the middle 80%), so it shows only where the cards say so: from 10 providers.
const most = computed(() => {
  const { p10, p90, providers } = props.summary
  return providers >= MIN_PROVIDERS_FOR_RANGE && p10 != null && p90 != null ? { low: p10, high: p90 } : null
})

const label = computed(() => {
  const { min, max } = props.summary
  const parts = [`Lowest ${dollars(min)}, highest ${dollars(max)}.`]
  if (most.value) parts.push(rangeLine(props.summary)!)
  if (typical.value != null) parts.push(`Typical price: ${dollars(typical.value)}.`)
  if (props.marker != null) parts.push(`Marked: ${money(props.marker)}.`)
  return parts.join(' ')
})
</script>

<template>
  <div>
    <div class="range-bar" role="img" :aria-label="label">
      <div class="range-bar__track" />
      <div v-if="most" class="range-bar__band" :style="{ left: position(most.low), width: width(most.low, most.high) }" />
      <div v-if="typical != null" class="range-bar__median" :style="{ left: position(typical) }" />
      <div v-if="marker != null" class="range-bar__marker" :style="{ left: position(marker) }" />
    </div>
    <div class="d-flex justify-space-between text-body-small text-medium-emphasis" aria-hidden="true">
      <span>Lowest {{ dollars(summary.min) }}</span>
      <span>Highest {{ dollars(summary.max) }}</span>
    </div>
  </div>
</template>

<style scoped>
.range-bar {
  position: relative;
  height: 16px;
  margin: 4px 6px;
}
.range-bar__track {
  position: absolute;
  inset: 7px 0 auto;
  height: 2px;
  background: rgba(var(--v-theme-on-surface), 0.2);
}
.range-bar__band {
  position: absolute;
  top: 3px;
  height: 10px;
  min-width: 2px;
  border-radius: 3px;
  background: rgba(var(--v-theme-primary), 0.35);
}
.range-bar__median {
  position: absolute;
  top: 0;
  width: 2px;
  height: 16px;
  margin-left: -1px;
  background: rgb(var(--v-theme-primary));
}
.range-bar__marker {
  position: absolute;
  top: 3px;
  width: 10px;
  height: 10px;
  margin-left: -5px;
  border-radius: 50%;
  border: 2px solid rgb(var(--v-theme-surface));
  background: rgb(var(--v-theme-copper));
}
</style>
