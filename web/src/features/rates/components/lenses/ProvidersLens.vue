<script setup lang="ts">
import { computed } from 'vue'
import { plural } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { hasProviderFilter, pinsProvider, yourRateGroups, type YourRateGroup } from '../../lib/lenses'

const props = defineProps<{ result: RateSearchResult }>()
const emit = defineEmits<{ focusNpi: [] }>()

const SHOWN_LINES = 8
const filtered = computed(() => hasProviderFilter(props.result))
const pinned = computed(() => pinsProvider(props.result))
const groups = computed(() => yourRateGroups(props.result))
// Whole groups, up to about eight lines, so a group's lines and its "showing 5 of 12" note stay together.
const shownGroups = computed(() => {
  const shown: YourRateGroup[] = []
  let lines = 0
  for (const entry of groups.value) {
    if (shown.length && lines + entry.lines.length > SHOWN_LINES) break
    shown.push(entry)
    lines += entry.lines.length
  }
  return shown
})
// Counted in groups, not lines: a name can match several providers in one group.
const moreGroups = computed(() => groups.value.length - shownGroups.value.length)
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-providers">
    <template v-if="filtered">
      <p v-if="!pinned" class="text-body-medium">Providers whose name contains "{{ result.query.provider }}":</p>
      <div v-for="entry in shownGroups" :key="entry.groupKey" class="d-flex flex-column ga-1" data-testid="your-rate-group">
        <p class="text-body-small text-medium-emphasis">{{ entry.group }}</p>
        <div
          v-for="line in entry.lines"
          :key="`${line.groupKey}|${line.providerKey}`"
          class="yours-rule"
          data-testid="your-rate-line"
        >
          <p class="text-body-medium font-weight-medium">{{ line.provider }}</p>
          <p class="text-body-medium">{{ line.text }}</p>
        </div>
        <p v-if="entry.capNote" class="text-body-small text-medium-emphasis" data-testid="highlight-cap">{{ entry.capNote }}</p>
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
