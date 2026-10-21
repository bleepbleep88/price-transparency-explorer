<script setup lang="ts">
import { computed } from 'vue'
import { useDataStatus } from '../useDataStatus'

// Seen only while data loads or when something failed; hidden once the data is ready. The live region always stays, so
// the parent's class (spacing) goes on the chip.
defineOptions({ inheritAttrs: false })
const { status, progress, unreachable } = useDataStatus()

const view = computed<{ text: string; failed: boolean } | null>(() => {
  const current = status.value
  if (!current) return unreachable.value ? { text: "Can't reach the server", failed: true } : null
  if (current.phase === 'ready') {
    return current.issues.some((issue) => issue.severity === 'error') ? { text: 'Some data failed', failed: true } : null
  }
  if (current.phase === 'failed') return { text: 'Data failed to load', failed: true }
  // Every file is saved and searchable; the server is saving the numbers behind each answer.
  if (current.phase === 'buildingStatistics') return { text: 'Preparing comparisons', failed: false }
  const percent = progress.value == null ? '' : ` ${Math.round(progress.value * 100)}%`
  return { text: `Loading data${percent}`, failed: false }
})

// What the live region announces: phase changes only, never every percent.
const spoken = computed(() => {
  const current = status.value
  if (!current) return unreachable.value ? "Can't reach the server" : ''
  if (current.phase === 'ready') return view.value?.text ?? 'Data ready'
  if (current.phase === 'failed' || current.phase === 'buildingStatistics') return view.value?.text ?? ''
  return 'Loading data'
})
</script>

<template>
  <span class="d-sr-only" role="status">{{ spoken }}</span>
  <v-menu v-if="view" location="bottom end" :close-on-content-click="false">
    <template #activator="{ props }">
      <v-chip
        v-bind="{ ...props, ...$attrs }"
        :color="view.failed ? 'error' : undefined"
        variant="outlined"
        size="small"
        role="button"
        :aria-label="`Data status: ${view.text}`"
        data-testid="data-status"
      >
        {{ view.text }}
      </v-chip>
    </template>

    <v-card max-width="440" title="Data status">
      <v-card-text class="d-flex flex-column ga-2">
        <p>{{ status?.message ?? 'The server did not answer.' }}</p>
        <v-progress-linear v-if="progress != null" :model-value="progress * 100" color="primary" rounded height="6" />
        <p v-if="status && status.filesTotal > 0" class="text-medium-emphasis">{{ status.filesDone }} of {{ status.filesTotal }} files done</p>
        <ul v-if="status?.issues.length" class="issues">
          <li v-for="(issue, index) in status.issues" :key="index" :class="{ failed: issue.severity === 'error' }">{{ issue.message }}</li>
        </ul>
      </v-card-text>
    </v-card>
  </v-menu>
</template>

<style scoped>
.issues {
  padding-left: 16px;
  font-size: 0.875rem;
}

.failed {
  color: rgb(var(--v-theme-error));
}
</style>
