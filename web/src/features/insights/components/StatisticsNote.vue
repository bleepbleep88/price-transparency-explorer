<script setup lang="ts">
import { computed } from 'vue'
import type { StatisticsBasis } from '../api/types'
import { staleMessage, statisticsLine } from '../lib/insightText'

// Which snapshot an insight describes, and a warning while its saved statistics are missing or being rebuilt.
const props = defineProps<{ basis: StatisticsBasis }>()

const stale = computed(() => staleMessage(props.basis))
</script>

<template>
  <div class="d-flex flex-column ga-2" data-testid="statistics-note">
    <p class="text-body-small text-medium-emphasis">{{ statisticsLine(basis) }}</p>
    <v-alert v-if="stale" type="info" variant="tonal" density="compact" data-testid="statistics-stale">{{ stale }}</v-alert>
  </div>
</template>
