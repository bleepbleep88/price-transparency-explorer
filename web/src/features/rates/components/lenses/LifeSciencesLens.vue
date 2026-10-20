<script setup lang="ts">
import { computed } from 'vue'
import { wholeNumber } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { DRUG_EXAMPLES, isDrugCode, unitPriceLines, unitPriceText } from '../../lib/lenses'

const props = defineProps<{ result: RateSearchResult }>()
const emit = defineEmits<{ search: [code: string] }>()

const drug = computed(() => isDrugCode(props.result.code))
const lines = computed(() => (drug.value ? unitPriceLines(props.result) : []))
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-life-sciences">
    <template v-if="drug">
      <p class="text-body-medium">
        {{ result.code?.name ?? result.query.code }}: price per billing unit by site of care. The unit is set by the code's
        description, so compare like with like.
      </p>
      <v-table density="compact">
        <thead>
          <tr>
            <th>Site of care</th>
            <th>Plan</th>
            <th class="text-end">Providers</th>
            <th class="text-end">Rate</th>
            <th>Unit</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="line in lines" :key="line.groupKey" data-testid="unit-price-line">
            <td>{{ line.site }}</td>
            <td>{{ line.product }}</td>
            <td class="text-end">{{ wholeNumber(line.providers) }}</td>
            <td class="text-end">{{ unitPriceText(line) }}</td>
            <td class="text-body-small">{{ line.unit }}</td>
          </tr>
        </tbody>
      </v-table>
    </template>
    <template v-else>
      <p class="text-body-medium">
        This isn't a drug code. Drugs (HCPCS codes starting with J or Q) are priced per billing unit; try one of these:
      </p>
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
