<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiOpenInNew } from '@mdi/js'
import { plural } from '@/lib/format'
import type { SearchParams } from '@/features/rates/lib/searchParams'
import type { OutOfNetworkResult } from '../api/types'
import {
  OUT_OF_NETWORK_NOTES,
  PER_UNIT_NOTE,
  amountsListed,
  amountsSentence,
  amountsText,
  billedLine,
  fileNotes,
  groupLabel,
  noBilledLine,
  severalCodes,
  visibleGroups,
} from '../lib/outOfNetworkText'

const props = defineProps<{ result: OutOfNetworkResult; params: SearchParams }>()

// Five columns don't fit a phone: there each group is a short stack of lines instead.
const { smAndDown } = useDisplay()

const groups = computed(() => visibleGroups(props.result, props.params))
const withCode = computed(() => severalCodes(groups.value))
const notes = computed(() => (groups.value.some((group) => group.perUnitCode) ? [...OUT_OF_NETWORK_NOTES, PER_UNIT_NOTE] : OUT_OF_NETWORK_NOTES))
// A file read only in part, or not at all: said once, so a missing product doesn't read as "no amounts".
const problems = computed(() => fileNotes(props.result, props.params))
// The allowed-amount files behind the rows shown, for tracing.
const files = computed(() => props.result.files.filter((file) => groups.value.some((group) => group.productKey === file.productKey)))

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)
</script>

<template>
  <v-card data-testid="oon-panel">
    <v-card-item>
      <v-card-title tag="h2" class="text-wrap">Out of network</v-card-title>
      <v-card-subtitle class="text-wrap">
        What providers outside the plan's network billed for {{ result.query.code }}, and what the plan allowed.
      </v-card-subtitle>
    </v-card-item>

    <v-card-text class="d-flex flex-column ga-3">
      <p v-for="problem in problems" :key="problem" class="text-body-medium text-warning" data-testid="oon-file-note">{{ problem }}</p>

      <div v-if="smAndDown" class="d-flex flex-column ga-2">
        <v-sheet v-for="group in groups" :key="group.key" border rounded="sm" class="pa-3" data-testid="oon-row">
          <p class="text-title-small">{{ groupLabel(group, withCode) }}</p>
          <p v-if="group.places.length" class="text-body-small text-medium-emphasis">{{ group.places.map((place) => place.name).join(' · ') }}</p>
          <p class="text-body-medium font-weight-medium mt-1" data-testid="oon-share">{{ billedLine(group) ?? noBilledLine(group) }}</p>
          <p class="text-body-medium" data-testid="oon-allowed">{{ amountsSentence(group.allowed, 'Allowed amounts') }}</p>
          <p class="text-body-medium" data-testid="oon-billed">
            {{ group.billed ? amountsSentence(group.billed, 'Billed charges') : 'Billed charges not published.' }}
          </p>
          <p class="text-body-small text-medium-emphasis">{{ amountsListed(group.payments) }} for {{ plural(group.providers, 'provider') }}</p>
        </v-sheet>
      </div>

      <v-table v-else density="compact">
        <thead>
          <tr>
            <th>Product and fee</th>
            <th>Billed vs allowed</th>
            <th>Allowed amounts listed</th>
            <th>Billed charges listed</th>
            <th>Listed for</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="group in groups" :key="group.key" data-testid="oon-row">
            <td>
              <div class="text-body-medium">{{ groupLabel(group, withCode) }}</div>
              <div v-if="group.places.length" class="text-body-small text-medium-emphasis">
                {{ group.places.map((place) => place.name).join(' · ') }}
              </div>
            </td>
            <td data-testid="oon-share">
              <span v-if="billedLine(group)" class="font-weight-medium">{{ billedLine(group) }}</span>
              <span v-else class="text-medium-emphasis">{{ noBilledLine(group) }}</span>
            </td>
            <td data-testid="oon-allowed">{{ capitalize(amountsText(group.allowed)) }}</td>
            <td data-testid="oon-billed">{{ group.billed ? capitalize(amountsText(group.billed)) : 'Not published' }}</td>
            <td>
              {{ amountsListed(group.payments) }}
              <div class="text-body-small text-medium-emphasis">{{ plural(group.providers, 'provider') }}</div>
            </td>
          </tr>
        </tbody>
      </v-table>

      <ul class="text-body-small text-medium-emphasis ps-4" data-testid="oon-notes">
        <li v-for="note in notes" :key="note">{{ note }}</li>
      </ul>
    </v-card-text>
    <v-card-actions class="flex-wrap">
      <v-btn :to="{ name: 'sources' }" variant="text" color="primary" data-testid="oon-sources-link">How these files were loaded</v-btn>
      <v-btn
        v-for="file in files"
        :key="file.fileId"
        :href="file.url"
        target="_blank"
        rel="noopener"
        variant="text"
        size="small"
        :append-icon="mdiOpenInNew"
      >
        {{ file.productLabel }} file
      </v-btn>
    </v-card-actions>
  </v-card>
</template>
