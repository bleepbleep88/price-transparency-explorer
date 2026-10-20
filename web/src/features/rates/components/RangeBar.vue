<script setup lang="ts">
import { computed } from 'vue'
import { money } from '@/lib/format'
import type { RateStats } from '../api/types'

const props = defineProps<{ stats: RateStats; marker?: number | null }>()

const span = computed(() => Math.max(props.stats.max - props.stats.min, 0.01))
const position = (value: number) => `${(((value - props.stats.min) / span.value) * 100).toFixed(2)}%`
const width = (from: number, to: number) => `${(((to - from) / span.value) * 100).toFixed(2)}%`

const label = computed(() => {
  const { min, p25, median, p75, max } = props.stats
  const middle = p25 != null && p75 != null ? ` Middle half of providers: ${money(p25)} to ${money(p75)}.` : ''
  const typical = median != null ? ` Median ${money(median)}.` : ''
  return `Rates range from ${money(min)} to ${money(max)}.${middle}${typical}`
})
</script>

<template>
  <div>
    <div class="range-bar" role="img" :aria-label="label">
      <div class="range-bar__track" />
      <div
        v-if="stats.p25 != null && stats.p75 != null"
        class="range-bar__band"
        :style="{ left: position(stats.p25), width: width(stats.p25, stats.p75) }"
      />
      <div v-if="stats.median != null" class="range-bar__median" :style="{ left: position(stats.median) }" />
      <div v-if="marker != null" class="range-bar__marker" :style="{ left: position(marker) }" />
    </div>
    <div class="d-flex justify-space-between text-body-small text-medium-emphasis">
      <span>{{ money(stats.min) }}</span>
      <span>{{ money(stats.max) }}</span>
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
  background: rgb(var(--v-theme-warning));
}
</style>
