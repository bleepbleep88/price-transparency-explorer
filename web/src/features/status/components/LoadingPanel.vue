<script setup lang="ts">
import { mdiCloudDownloadOutline } from '@mdi/js'
import { useDataStatus } from '../useDataStatus'

const { status, progress } = useDataStatus()
</script>

<template>
  <v-card class="pa-2" data-testid="loading-panel">
    <v-card-item :prepend-icon="mdiCloudDownloadOutline" title="Rate data is still loading">
      <v-card-subtitle class="text-wrap">Results will appear here on their own as soon as loading finishes.</v-card-subtitle>
    </v-card-item>
    <v-card-text class="d-flex flex-column ga-2">
      <p class="text-body-2" aria-live="polite">{{ status?.message ?? 'Waiting for the server…' }}</p>
      <v-progress-linear
        :model-value="progress == null ? undefined : progress * 100"
        :indeterminate="progress == null"
        color="info"
        rounded
        height="8"
      />
    </v-card-text>
  </v-card>
</template>
