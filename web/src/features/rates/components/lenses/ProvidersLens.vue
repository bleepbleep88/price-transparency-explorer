<script setup lang="ts">
import { computed } from 'vue'
import { plural } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { hasProviderFilter, pinsProvider, yourRateLines } from '../../lib/lenses'

const props = defineProps<{ result: RateSearchResult }>()
const emit = defineEmits<{ focusNpi: [] }>()

const SHOWN = 8
const filtered = computed(() => hasProviderFilter(props.result))
const pinned = computed(() => pinsProvider(props.result))
const lines = computed(() => yourRateLines(props.result))
const shownLines = computed(() => lines.value.slice(0, SHOWN))
// Counted in groups, not lines: a name can match several providers in one group.
const moreGroups = computed(() => {
  const shown = new Set(shownLines.value.map((line) => line.groupKey))
  return new Set(lines.value.slice(SHOWN).map((line) => line.groupKey).filter((key) => !shown.has(key))).size
})
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-providers">
    <template v-if="filtered">
      <p v-if="!pinned" class="text-body-medium">Providers whose name contains "{{ result.query.provider }}":</p>
      <div
        v-for="line in shownLines"
        :key="`${line.groupKey}|${line.providerKey}`"
        class="yours-rule"
        data-testid="your-rate-line"
      >
        <p class="text-body-small text-medium-emphasis">{{ line.group }}</p>
        <p class="text-body-medium font-weight-medium">{{ line.provider }}</p>
        <p class="text-body-medium">{{ line.text }}</p>
      </div>
      <p v-if="moreGroups" class="text-body-small text-medium-emphasis" data-testid="more-groups">
        {{ plural(moreGroups, 'more group') }} in the cards below.
      </p>
      <p class="text-body-small text-medium-emphasis">The gap to the lower quartile on each card is negotiating context.</p>
    </template>
    <template v-else>
      <p class="text-body-medium">Add your NPI or EIN to see where your rate sits among other providers for this service.</p>
      <v-btn class="align-self-start" variant="tonal" color="primary" data-testid="focus-npi" @click="emit('focusNpi')">
        Enter your NPI
      </v-btn>
    </template>
  </div>
</template>
