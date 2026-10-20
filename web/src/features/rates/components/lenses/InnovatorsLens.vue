<script setup lang="ts">
import { computed } from 'vue'
import { useClipboard } from '@vueuse/core'
import { mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import type { RateSearchResult } from '../../api/types'
import { API_REFERENCE_LINKS, apiSearchUrl, qualityLines } from '../../lib/lenses'
import type { SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()

const { copy, copied } = useClipboard({ legacy: true, copiedDuring: 1500 })
const lines = computed(() => qualityLines(props.result.quality))
const url = computed(() => apiSearchUrl(props.params, window.location.origin))
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-innovators">
    <ul class="text-body-medium ps-4">
      <li v-for="line in lines" :key="line">{{ line }}</li>
    </ul>
    <p class="text-body-medium">
      Every row links to its source: the magnifier in the Source column opens the file, the JSON path to the exact price,
      and the file's SHA-256.
    </p>
    <div>
      <p class="text-body-small text-medium-emphasis mb-1">This search as an API request</p>
      <div class="d-flex align-center ga-1">
        <code class="text-body-small text-break flex-grow-1" data-testid="api-url">{{ url }}</code>
        <v-btn :icon="mdiContentCopy" variant="text" size="small" aria-label="Copy the API request" @click="copy(url)" />
      </div>
      <p v-if="copied" class="text-body-small text-success" role="status">Copied</p>
    </div>
    <div class="d-flex flex-wrap ga-2">
      <v-btn
        v-for="link in API_REFERENCE_LINKS"
        :key="link.href"
        :href="link.href"
        target="_blank"
        rel="noopener"
        variant="outlined"
        size="small"
        :append-icon="mdiOpenInNew"
      >
        {{ link.label }}
      </v-btn>
    </div>
  </div>
</template>
