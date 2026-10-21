<script setup lang="ts">
import { computed } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import { plural } from '@/lib/format'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { OutOfNetworkResult } from '../api/types'
import {
  OUT_OF_NETWORK_NOTES,
  PER_UNIT_NOTE,
  amountsListed,
  amountsSentence,
  billedLine,
  fileNotes,
  groupLabel,
  noBilledLine,
  outOfNetworkLine,
  outOfNetworkWhich,
  severalCodes,
  visibleGroups,
} from '../lib/outOfNetworkText'

const props = defineProps<{ result: OutOfNetworkResult; params: SearchParams }>()

const groups = computed(() => visibleGroups(props.result, props.params))
// One line up front, for the bill with the most providers; every bill, note and file sits in Details.
const line = computed(() => outOfNetworkLine(groups.value))
const which = computed(() => outOfNetworkWhich(groups.value))
const withCode = computed(() => severalCodes(groups.value))
const notes = computed(() => (groups.value.some((group) => group.perUnitCode) ? [...OUT_OF_NETWORK_NOTES, PER_UNIT_NOTE] : OUT_OF_NETWORK_NOTES))
// A file read only in part, or not at all, so a missing product doesn't read as "no amounts".
const problems = computed(() => fileNotes(props.result, props.params))
// The allowed-amount files behind the bills shown, for tracing.
const files = computed(() => props.result.files.filter((file) => groups.value.some((group) => group.productKey === file.productKey)))
</script>

<template>
  <section class="oon" aria-labelledby="oon-title" data-testid="oon-panel">
    <h2 id="oon-title" class="oon-title">Out of network</h2>
    <p v-if="which" class="oon-which" data-testid="oon-which">{{ which }}</p>
    <p class="oon-line" data-testid="oon-line">{{ line }}</p>

    <DetailsPanel testid="oon-details">
      <p v-for="problem in problems" :key="problem" class="problem" data-testid="oon-file-note">{{ problem }}</p>
      <div v-for="group in groups" :key="group.key" class="group" data-testid="oon-row">
        <p class="group-title">{{ groupLabel(group, withCode) }}</p>
        <p v-if="group.places.length" class="secondary">{{ group.places.map((place) => place.name).join(' · ') }}</p>
        <p data-testid="oon-share">{{ billedLine(group) ?? noBilledLine(group) }}</p>
        <p data-testid="oon-allowed">{{ amountsSentence(group.allowed, 'Allowed amounts') }}</p>
        <p data-testid="oon-billed">{{ group.billed ? amountsSentence(group.billed, 'Billed charges') : 'Billed charges not published.' }}</p>
        <p class="secondary">{{ amountsListed(group.payments) }} for {{ plural(group.providers, 'provider') }}</p>
      </div>
      <ul class="notes" data-testid="oon-notes">
        <li v-for="note in notes" :key="note">{{ note }}</li>
      </ul>
      <div class="links">
        <router-link :to="{ name: 'sources' }" data-testid="oon-sources-link">How these files were loaded</router-link>
        <a v-for="file in files" :key="file.fileId" :href="file.url" target="_blank" rel="noopener">
          {{ file.productLabel }} file <v-icon :icon="mdiOpenInNew" size="16" aria-hidden="true" />
        </a>
      </div>
    </DetailsPanel>
  </section>
</template>

<style scoped>
.oon {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.oon-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.oon-line {
  font-size: 1rem;
}

.oon-which,
.secondary {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.group-title {
  font-weight: 600;
}

.problem {
  color: rgb(var(--v-theme-error));
}

.notes {
  padding-left: 16px;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.links {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
}

.links a {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: rgb(var(--v-theme-primary));
}
</style>
