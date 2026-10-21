<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiClose } from '@mdi/js'
import { plural } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import { ApiError } from '@/lib/apiClient'
import { providerName } from '@/lib/plainNames'
import type { RateRow } from '../api/types'
import { useRateNpis } from '../composables/useRateQueries'
import { useReturnFocus } from '../composables/useReturnFocus'
import { formatNpi } from '../lib/providerIds'

// The clinicians (NPIs) the plan lists for a row, as published. A row's details: the main view never shows NPIs.
const open = defineModel<boolean>({ required: true })
useReturnFocus(open)
const props = defineProps<{ row: RateRow | null }>()

const PAGE_SIZE = 50
const page = ref(1)
watch(
  () => props.row,
  () => {
    page.value = 1
  },
)

// The row's own rate first (it must exist for the request path), then the other rates behind the row.
const rateIds = computed(() =>
  open.value && props.row ? [props.row.firstRateId, ...props.row.rateIds.filter((id) => id !== props.row!.firstRateId)] : null,
)
const npis = useRateNpis(rateIds, () => props.row?.providerKey ?? null, page, PAGE_SIZE)
const pages = computed(() => Math.max(1, Math.ceil((npis.data.value?.total ?? 0) / PAGE_SIZE)))
const errorMessage = computed(() => (npis.error.value instanceof ApiError ? npis.error.value.message : npis.error.value ? "Couldn't load the NPIs." : null))
const showRates = computed(() => props.row?.multipleRates ?? false)

function rateText(rate: number, rateHigh: number): string {
  return rate === rateHigh ? dollars(rate) : `${dollars(rate)} – ${dollars(rateHigh)}`
}
</script>

<template>
  <v-dialog v-model="open" max-width="520" scrollable>
    <v-card data-testid="npi-dialog">
      <div class="d-flex align-start pa-4 pb-0">
        <div class="flex-grow-1">
          <h2 class="dialog-title">{{ row ? providerName(row.businessName) : 'Clinicians' }}</h2>
          <p v-if="row" class="muted">Tax ID {{ row.einDisplay }}</p>
        </div>
        <v-btn :icon="mdiClose" variant="text" aria-label="Close" @click="open = false" />
      </div>
      <v-card-text class="d-flex flex-column ga-2">
        <v-skeleton-loader v-if="npis.isPending.value" type="list-item@4" />
        <v-alert v-else-if="errorMessage" type="error" variant="tonal">{{ errorMessage }}</v-alert>
        <template v-else-if="npis.data.value">
          <h3 class="list-title" data-testid="npi-count">{{ plural(npis.data.value.total, 'clinician') }} (NPIs)</h3>
          <v-list density="compact" class="py-0">
            <v-list-item
              v-for="item in npis.data.value.items"
              :key="item.npi"
              :title="formatNpi(item.npi)"
              :subtitle="showRates ? rateText(item.rate, item.rateHigh) : undefined"
            >
              <template v-if="!item.valid" #append>
                <span class="invalid" title="Fails the NPI check. Shown as published.">Not a valid NPI</span>
              </template>
            </v-list-item>
          </v-list>
          <v-pagination v-if="pages > 1" v-model="page" :length="pages" density="compact" />
        </template>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.dialog-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.muted {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.list-title {
  font-family: var(--v-font-body);
  font-size: 1rem;
  font-weight: 600;
}

.invalid {
  font-size: 0.875rem;
  color: rgb(var(--v-theme-high));
}
</style>
