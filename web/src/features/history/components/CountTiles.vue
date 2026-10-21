<script setup lang="ts">
import { computed } from 'vue'
import { wholeNumber } from '@/lib/format'
import type { ChangeCounts, ChangeFilter } from '../api/types'

// The changes page's answer (decision 90): four big counts. Each is also the filter for the list under it.
const props = defineProps<{ counts: ChangeCounts; kind: ChangeFilter | null | undefined }>()
const emit = defineEmits<{ kind: [kind: ChangeFilter | null] }>()

const tiles = computed(() => [
  { kind: 'increased' as const, label: 'Went up', count: props.counts.increased, tone: 'high' },
  { kind: 'decreased' as const, label: 'Went down', count: props.counts.decreased, tone: 'low' },
  { kind: 'new' as const, label: 'New', count: props.counts.new, tone: '' },
  { kind: 'dropped' as const, label: 'Gone', count: props.counts.dropped, tone: '' },
])
</script>

<template>
  <div class="count-tiles" role="group" aria-label="Kind of change">
    <button
      v-for="tile in tiles"
      :key="tile.kind"
      type="button"
      class="count-tile"
      :class="{ selected: kind === tile.kind }"
      :aria-pressed="kind === tile.kind"
      :data-testid="`changes-kind-${tile.kind}`"
      @click="emit('kind', kind === tile.kind ? null : tile.kind)"
    >
      <span class="label">{{ tile.label }}</span>
      <span class="count" :class="tile.tone">{{ wholeNumber(tile.count) }}</span>
    </button>
  </div>
</template>

<style scoped>
.count-tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 16px;
}

@media (max-width: 599px) {
  .count-tiles {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }
}

.count-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  padding: 16px;
  text-align: left;
  color: inherit;
  font: inherit;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
  cursor: pointer;
}

.count-tile:hover {
  border-color: rgba(var(--v-theme-primary), 0.6);
}

.count-tile.selected {
  border-color: rgb(var(--v-theme-primary));
  box-shadow: inset 0 0 0 1px rgb(var(--v-theme-primary));
}

.count-tile:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.label {
  font-size: 0.875rem;
  font-weight: 600;
}

.count {
  font-family: var(--v-font-heading);
  font-size: 2.5rem;
  font-weight: 600;
  line-height: 1.1;
}

.count.high {
  color: rgb(var(--v-theme-high));
}

.count.low {
  color: rgb(var(--v-theme-low));
}
</style>
