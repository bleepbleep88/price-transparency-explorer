<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { mdiCheck, mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import { copyText } from '@/lib/clipboard'
import type { RateSearchResult } from '../../api/types'
import { API_REFERENCE_LINKS, apiSearchUrl, qualityLines } from '../../lib/lenses'
import type { SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()

const lines = computed(() => qualityLines(props.result.quality))
const url = computed(() => apiSearchUrl(props.params, window.location.origin))

// The check mark stays until the URL changes: no timer clears it.
const copied = ref(false)
watch(
  () => props.params,
  () => {
    copied.value = false
  },
)

async function copyUrl() {
  // Empty, then filled, so a second copy is announced again.
  copied.value = false
  await nextTick()
  copied.value = await copyText(url.value)
}
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-innovators">
    <div>
      <p class="text-body-medium">Across the comparison groups shown:</p>
      <ul class="text-body-medium ps-4">
        <li v-for="line in lines" :key="line">{{ line }}</li>
      </ul>
    </div>
    <p class="text-body-medium">
      Every row links to its source: the magnifier in the Source column shows the file, the code and rate as published, the
      JSON path to the exact price, and the file's SHA-256.
    </p>
    <div>
      <p class="text-body-small text-medium-emphasis mb-1">This search as an API request</p>
      <div class="d-flex align-center ga-1">
        <code class="text-body-small text-break flex-grow-1" data-testid="api-url">{{ url }}</code>
        <v-btn
          :icon="copied ? mdiCheck : mdiContentCopy"
          variant="text"
          size="small"
          :color="copied ? 'success' : undefined"
          :aria-label="copied ? 'Copied the API request' : 'Copy the API request'"
          data-testid="copy-api-url"
          @click="copyUrl"
        />
      </div>
      <p class="d-sr-only" role="status" data-testid="copy-status">{{ copied ? 'Copied the API request' : '' }}</p>
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
