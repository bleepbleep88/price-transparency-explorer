<script setup lang="ts">
import { computed } from 'vue'
import { useDataStatus } from '@/features/status/useDataStatus'
import type { StatisticsBasis } from '../api/types'
import { staleMessage, statisticsLine } from '../lib/insightText'

// Which plan files an insight describes, and a warning while its saved figures are missing or out of date.
const props = defineProps<{ basis: StatisticsBasis }>()

// The page refreshes itself only when a load or rebuild finishes (the data status turns ready), so the note promises a
// refresh only while one is running.
const { status } = useDataStatus()
const rebuilding = computed(() => status.value != null && status.value.phase !== 'ready' && status.value.phase !== 'failed')
const stale = computed(() => staleMessage(props.basis, rebuilding.value))
</script>

<template>
  <div class="d-flex flex-column ga-2" data-testid="statistics-note">
    <p class="text-body-small text-medium-emphasis">{{ statisticsLine(basis) }}</p>
    <v-alert v-if="stale" type="info" variant="tonal" density="compact" data-testid="statistics-stale">{{ stale }}</v-alert>
  </div>
</template>
