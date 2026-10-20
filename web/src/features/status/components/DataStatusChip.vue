<script setup lang="ts">
import { computed } from 'vue'
import { mdiAlertCircleOutline, mdiCheckCircleOutline, mdiCloudDownloadOutline, mdiLanConnect } from '@mdi/js'
import { useDataStatus } from '../useDataStatus'

const { status, progress } = useDataStatus()

const view = computed(() => {
  const current = status.value
  if (!current) return { color: 'default', icon: mdiLanConnect, text: 'Connecting…' }
  if (current.phase === 'ready') {
    return current.issues.length > 0
      ? { color: 'warning', icon: mdiAlertCircleOutline, text: 'Data ready, with notes' }
      : { color: 'success', icon: mdiCheckCircleOutline, text: 'Data ready' }
  }
  if (current.phase === 'failed') return { color: 'error', icon: mdiAlertCircleOutline, text: 'Data failed to load' }
  const percent = progress.value == null ? '' : ` ${Math.round(progress.value * 100)}%`
  return { color: 'info', icon: mdiCloudDownloadOutline, text: `Loading data${percent}` }
})
</script>

<template>
  <v-menu location="bottom end" :close-on-content-click="false">
    <template #activator="{ props }">
      <v-chip
        v-bind="props"
        :color="view.color"
        :prepend-icon="view.icon"
        variant="tonal"
        size="small"
        role="status"
        aria-live="polite"
        data-testid="data-status"
      >
        {{ view.text }}
      </v-chip>
    </template>

    <v-card max-width="440" title="Data loading">
      <v-card-text class="d-flex flex-column ga-2">
        <p class="text-body-medium">{{ status?.message ?? 'Waiting for the server…' }}</p>
        <v-progress-linear v-if="progress != null" :model-value="progress * 100" color="info" rounded height="6" />
        <p v-if="status && status.filesTotal > 0" class="text-body-small text-medium-emphasis">
          {{ status.filesDone }} of {{ status.filesTotal }} rate files processed
        </p>
        <template v-if="status?.issues.length">
          <v-divider />
          <p class="text-body-small font-weight-medium">Notes from loading</p>
          <v-alert
            v-for="(issue, index) in status.issues"
            :key="index"
            :type="issue.severity === 'error' ? 'error' : 'warning'"
            variant="tonal"
            density="compact"
            class="text-body-small"
          >
            {{ issue.message }}
          </v-alert>
        </template>
      </v-card-text>
    </v-card>
  </v-menu>
</template>
