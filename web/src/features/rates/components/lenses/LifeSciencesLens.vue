<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { wholeNumber } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { DRUG_EXAMPLES, hasFacilityCharges, isDrugCode, unitPriceLines, unitPriceText } from '../../lib/lenses'

const props = defineProps<{ result: RateSearchResult }>()
const emit = defineEmits<{ search: [code: string] }>()

const { smAndDown } = useDisplay()
const drug = computed(() => isDrugCode(props.result.code))
const lines = computed(() => (drug.value ? unitPriceLines(props.result) : []))
const facilityCharges = computed(() => drug.value && hasFacilityCharges(props.result))
const unitDefinition = computed(() => props.result.code?.description || props.result.code?.name || props.result.query.code)

const headers = [
  { title: 'Site of care', key: 'site', sortable: false },
  { title: 'Product', key: 'product', sortable: false },
  { title: 'Providers', key: 'providers', sortable: false, align: 'end' as const },
  { title: 'Price per unit', key: 'rate', sortable: false, align: 'end' as const },
]
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-life-sciences">
    <template v-if="drug">
      <template v-if="lines.length">
        <p class="text-body-medium">One unit, as the code defines it: {{ unitDefinition }}.</p>
        <v-data-table
          :headers="headers"
          :items="lines"
          item-value="groupKey"
          :items-per-page="-1"
          hide-default-footer
          density="compact"
          :mobile="smAndDown"
          :row-props="() => ({ 'data-testid': 'unit-price-line' })"
        >
          <template #[`item.providers`]="{ item }">{{ wholeNumber(item.providers) }}</template>
          <template #[`item.rate`]="{ item }">{{ unitPriceText(item) }}</template>
        </v-data-table>
      </template>
      <p v-else class="text-body-medium">No per-unit prices for this code here.</p>
      <p v-if="facilityCharges" class="text-body-small text-medium-emphasis" data-testid="facility-charges-note">
        Facility charges filed under revenue codes aren't per-unit prices; they're on the cards below.
      </p>
    </template>
    <template v-else>
      <p class="text-body-medium">This isn't a drug code. Drugs are priced per billing unit; try one of these:</p>
      <div class="d-flex flex-wrap ga-2">
        <v-btn
          v-for="example in DRUG_EXAMPLES"
          :key="example.code"
          variant="outlined"
          size="small"
          :data-testid="`drug-example-${example.code}`"
          @click="emit('search', example.code)"
        >
          {{ example.code }}: {{ example.label }}
        </v-btn>
      </div>
    </template>
  </div>
</template>
