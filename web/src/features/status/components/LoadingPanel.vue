<script setup lang="ts">
import { computed } from 'vue'
import { useDataStatus } from '../useDataStatus'

// One line and a bar while the plan's files load; the page fills in by itself when they're ready.
const { status, progress } = useDataStatus()

const line = computed(() => {
  if (status.value?.phase === 'buildingStatistics') return 'Preparing comparisons'
  const percent = progress.value == null ? '' : ` ${Math.round(progress.value * 100)}%`
  return `Loading prices${percent}`
})
</script>

<template>
  <section class="loading" data-testid="loading-panel">
    <p class="loading-line">{{ line }}</p>
    <v-progress-linear
      :model-value="progress == null ? undefined : progress * 100"
      :indeterminate="progress == null"
      color="primary"
      rounded
      height="6"
      aria-label="Loading progress"
    />
  </section>
</template>

<style scoped>
.loading {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px 0;
}

.loading-line {
  font-size: 1rem;
  font-weight: 600;
}
</style>
