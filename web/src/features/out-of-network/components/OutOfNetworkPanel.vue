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
  amountHeadline,
  amountRange,
  groupLabel,
  percent,
  severalCodes,
  visibleGroups,
} from '../lib/outOfNetworkText'

const props = defineProps<{ result: OutOfNetworkResult; params: SearchParams }>()

// Five columns don't fit a phone: there each group is a short stack of lines instead.
const { smAndDown } = useDisplay()

const groups = computed(() => visibleGroups(props.result, props.params))
const withCode = computed(() => severalCodes(groups.value))
const notes = computed(() => (groups.value.some((group) => group.perUnitCode) ? [...OUT_OF_NETWORK_NOTES, PER_UNIT_NOTE] : OUT_OF_NETWORK_NOTES))
// The allowed-amount files behind the rows shown, for tracing.
const files = computed(() => props.result.files.filter((file) => groups.value.some((group) => group.productKey === file.productKey)))
</script>

<template>
  <v-card data-testid="oon-panel">
    <v-card-item>
      <v-card-title tag="h2" class="text-wrap">Out of network</v-card-title>
      <v-card-subtitle class="text-wrap">
        What the plan allowed when it paid providers outside its network for {{ result.query.code }}.
      </v-card-subtitle>
    </v-card-item>

    <v-card-text class="d-flex flex-column ga-3">
      <div v-if="smAndDown" class="d-flex flex-column ga-2">
        <v-sheet v-for="group in groups" :key="group.key" border rounded="sm" class="pa-3" data-testid="oon-row">
          <p class="text-title-small">{{ groupLabel(group, withCode) }}</p>
          <p v-if="group.places.length" class="text-body-small text-medium-emphasis">{{ group.places.map((place) => place.name).join(' · ') }}</p>
          <p class="text-body-medium mt-1">{{ plural(group.payments, 'payment') }} to {{ plural(group.providers, 'provider') }}</p>
          <p class="text-body-medium" data-testid="oon-allowed">
            Allowed {{ amountHeadline(group.allowed) }}<template v-if="amountRange(group.allowed)"> ({{ amountRange(group.allowed) }})</template>
          </p>
          <p class="text-body-medium" data-testid="oon-billed">
            <template v-if="group.billed">
              Billed {{ amountHeadline(group.billed) }}<template v-if="amountRange(group.billed)"> ({{ amountRange(group.billed) }})</template>
            </template>
            <template v-else>Billed charges not published</template>
          </p>
          <p v-if="group.allowedShareOfBilled != null" class="text-body-medium" data-testid="oon-share">
            Allowed ÷ billed: median {{ percent(group.allowedShareOfBilled) }}
          </p>
        </v-sheet>
      </div>

      <v-table v-else density="compact">
        <thead>
          <tr>
            <th>Product and fee</th>
            <th>Payments</th>
            <th>Allowed</th>
            <th>Billed</th>
            <th class="text-no-wrap">Allowed ÷ billed</th>
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
            <td class="text-no-wrap">
              {{ plural(group.payments, 'payment') }}
              <div class="text-body-small text-medium-emphasis">{{ plural(group.providers, 'provider') }}</div>
            </td>
            <td data-testid="oon-allowed">
              {{ amountHeadline(group.allowed) }}
              <div v-if="amountRange(group.allowed)" class="text-body-small text-medium-emphasis">{{ amountRange(group.allowed) }}</div>
            </td>
            <td data-testid="oon-billed">
              <template v-if="group.billed">
                {{ amountHeadline(group.billed) }}
                <div v-if="amountRange(group.billed)" class="text-body-small text-medium-emphasis">{{ amountRange(group.billed) }}</div>
              </template>
              <template v-else>Not published</template>
            </td>
            <td class="text-no-wrap" data-testid="oon-share">
              <template v-if="group.allowedShareOfBilled != null">median {{ percent(group.allowedShareOfBilled) }}</template>
              <span v-else class="text-medium-emphasis">too few to say</span>
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
