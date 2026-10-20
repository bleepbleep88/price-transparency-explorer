<script setup lang="ts">
import { computed } from 'vue'
import { plural } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { hasProviderFilter, yourRateLines } from '../../lib/lenses'

const props = defineProps<{ result: RateSearchResult }>()
const emit = defineEmits<{ focusNpi: [] }>()

const SHOWN = 8
const filtered = computed(() => hasProviderFilter(props.result))
const lines = computed(() => yourRateLines(props.result))
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-providers">
    <template v-if="filtered">
      <p v-if="!lines.length" class="text-body-medium">No comparison group here lists your provider.</p>
      <div v-for="line in lines.slice(0, SHOWN)" :key="`${line.groupKey}-${line.provider}`" class="yours-rule" data-testid="your-rate-line">
        <p class="text-body-small text-medium-emphasis">{{ line.group }}</p>
        <p class="text-body-medium font-weight-medium">{{ line.provider }}</p>
        <p class="text-body-medium">{{ line.text }}</p>
      </div>
      <p v-if="lines.length > SHOWN" class="text-body-small text-medium-emphasis">
        {{ plural(lines.length - SHOWN, 'more group lists', 'more groups list') }} your provider; see the cards below.
      </p>
      <p class="text-body-small text-medium-emphasis">
        The gap to the lower quartile on each card is negotiating context: how far a typical rate sits above the cheaper
        quarter of providers.
      </p>
    </template>
    <template v-else>
      <p class="text-body-medium">Add your NPI or EIN to see where your rate sits among other providers for this service.</p>
      <v-btn class="align-self-start" variant="tonal" color="primary" data-testid="focus-npi" @click="emit('focusNpi')">
        Enter your NPI
      </v-btn>
    </template>
  </div>
</template>
