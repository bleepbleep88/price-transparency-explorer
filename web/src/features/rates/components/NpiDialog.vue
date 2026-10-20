<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiClose } from '@mdi/js'
import { money, plural } from '@/lib/format'
import { ApiError } from '@/lib/apiClient'
import type { RateRow } from '../api/types'
import { useRateNpis } from '../composables/useRateQueries'
import { formatNpi } from '../lib/providerIds'

const open = defineModel<boolean>({ required: true })
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
const errorMessage = computed(() => (npis.error.value instanceof ApiError ? npis.error.value.message : npis.error.value ? 'Could not load the NPIs.' : null))
const showRates = computed(() => props.row?.multipleRates ?? false)

function rateText(rate: number, rateHigh: number): string {
  return rate === rateHigh ? money(rate) : `${money(rate)}–${money(rateHigh)}`
}
</script>

<template>
  <v-dialog v-model="open" max-width="560" scrollable>
    <v-card :title="row?.businessName ?? 'NPIs'" :subtitle="row ? `EIN ${row.einDisplay}` : undefined">
      <template #append>
        <v-btn :icon="mdiClose" variant="text" aria-label="Close" @click="open = false" />
      </template>
      <v-card-text>
        <p class="text-body-medium text-medium-emphasis mb-2">
          <template v-if="showRates">
            Individual providers (NPIs) the plan lists under this tax ID for these rates, with the rate each one is listed for.
          </template>
          <template v-else>Individual providers (NPIs) the plan lists under this tax ID for this rate.</template>
        </p>
        <v-skeleton-loader v-if="npis.isPending.value" type="list-item@4" />
        <v-alert v-else-if="errorMessage" type="error" variant="tonal">{{ errorMessage }}</v-alert>
        <template v-else-if="npis.data.value">
          <p class="text-body-small mb-1">{{ plural(npis.data.value.total, 'NPI') }}</p>
          <v-list density="compact" class="py-0">
            <v-list-item
              v-for="item in npis.data.value.items"
              :key="item.npi"
              :title="formatNpi(item.npi)"
              :subtitle="showRates ? rateText(item.rate, item.rateHigh) : undefined"
            >
              <template v-if="!item.valid" #append>
                <v-tooltip text="This number fails the standard NPI check, so the plan's file may have a typo. Shown as published." location="top">
                  <template #activator="{ props: tip }">
                    <v-chip v-bind="tip" size="x-small" color="warning" variant="tonal">Not a valid NPI</v-chip>
                  </template>
                </v-tooltip>
              </template>
            </v-list-item>
          </v-list>
          <v-pagination v-if="pages > 1" v-model="page" :length="pages" density="compact" class="mt-2" />
        </template>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>
