<script setup lang="ts">
import { computed } from 'vue'
import PriceTile from '@/components/simple/PriceTile.vue'
import type { RateTrendResult } from '@/features/history/api/types'
import { trendFor } from '@/features/history/lib/trend'
import { trendChipText } from '@/features/history/lib/trendChip'
import type { RateSearchResult } from '../api/types'
import { ALL_IN } from '@/lib/plainNames'
import { planTabs, tileFor, tileGroups } from '../lib/answer'

// The answer: one tile per bill for the plan picked, the doctor's and the place's. Picking a tile shows its places.
const props = defineProps<{
  result: RateSearchResult
  plan: string | null
  selected: string | null
  trend?: RateTrendResult | null
}>()
const emit = defineEmits<{ plan: [plan: string]; select: [groupKey: string] }>()

const tabs = computed(() => planTabs(props.result))
const groups = computed(() => tileGroups(props.result, props.plan, props.selected))
// "Each is a separate bill" only holds when no tile is an all-in visit price (a health center's).
const separateBills = computed(() => groups.value.length > 1 && groups.value.every((group) => !ALL_IN.has(group.settingKey)))
const tiles = computed(() =>
  groups.value.map((group) => ({
    ...tileFor(group, props.result),
    chip: trendChipText(trendFor(props.trend, group.key), props.trend?.selected.snapshotId ?? null),
  })),
)
</script>

<template>
  <section class="bills" aria-label="Prices">
    <v-btn-toggle
      v-if="tabs.length > 1"
      :model-value="plan"
      mandatory
      divided
      variant="outlined"
      density="comfortable"
      color="primary"
      class="plan-tabs"
      aria-label="Plan"
      data-testid="plan-tabs"
      @update:model-value="(value: string | null) => value && emit('plan', value)"
    >
      <v-btn v-for="tab in tabs" :key="tab.key" :value="tab.key" :data-testid="`plan-${tab.key}`">{{ tab.label }}</v-btn>
    </v-btn-toggle>

    <div class="tiles">
      <div v-for="tile in tiles" :key="tile.groupKey" class="tile-cell">
        <PriceTile
          :label="tile.label"
          :where="tile.where"
          :price="tile.price"
          :per-unit="tile.perUnit"
          :line="tile.line"
          :listed="tile.listed"
          :chip="tile.chip"
          :count="tile.count"
          :selected="tile.groupKey === selected"
          :selectable="tiles.length > 1"
          @select="emit('select', tile.groupKey)"
        />
        <p v-if="tile.yours" class="yours" data-testid="tile-yours">{{ tile.yours }}</p>
      </div>
    </div>
    <p v-if="tiles.length > 1" class="hint">{{ separateBills ? 'Each is a separate bill. ' : '' }}Pick one to see its places.</p>
  </section>
</template>

<style scoped>
.bills {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.plan-tabs {
  align-self: flex-start;
}

.tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
}

.tile-cell {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.tile-cell > :first-child {
  flex: 1 1 auto;
}

.yours {
  border-left: 3px solid rgb(var(--v-theme-copper));
  padding-left: 8px;
  font-size: 0.875rem;
}

.hint {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}
</style>
