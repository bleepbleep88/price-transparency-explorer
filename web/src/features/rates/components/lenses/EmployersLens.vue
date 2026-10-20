<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { money, plural, wholeDollars, wholeNumber } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { costLines, EMPLOYER_CAVEATS, yearlyLead, type CostLine } from '../../lib/lenses'
import { MAX_CASES, type SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()
const emit = defineEmits<{ cases: [cases: number | null] }>()

const SHOWN = 8
const { smAndDown } = useDisplay()
const lines = computed(() => costLines(props.result, props.params.cases))
const shown = computed(() => lines.value.slice(0, SHOWN))
const anyPerUnit = computed(() => lines.value.some((line) => line.perUnit))
// Cases a year only make sense for prices per case.
const anyPerCase = computed(() => lines.value.some((line) => !line.perUnit))
const lead = computed(() => yearlyLead(lines.value, props.params.group))

const headers = [
  { title: 'Comparison group', key: 'group', sortable: false },
  { title: 'Providers', key: 'providers', sortable: false, align: 'end' as const },
  { title: 'Median', key: 'median', sortable: false, align: 'end' as const },
  { title: 'Gap to the lower quartile', key: 'gap', sortable: false, align: 'end' as const },
]

function price(value: number, line: CostLine): string {
  return `${money(value)}${line.perUnit ? ' per unit' : ''}`
}

function onCases(value: number | string | null) {
  const cases = Number(value)
  emit('cases', Number.isInteger(cases) && cases >= 1 && cases <= MAX_CASES ? cases : null)
}
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-employers">
    <p v-if="!lines.length" class="text-body-medium">No group here has enough providers for a median.</p>
    <template v-else>
      <v-data-table
        :headers="headers"
        :items="shown"
        item-value="groupKey"
        :items-per-page="-1"
        hide-default-footer
        density="compact"
        :mobile="smAndDown"
        :row-props="() => ({ 'data-testid': 'cost-line' })"
      >
        <template #[`item.providers`]="{ item }">{{ wholeNumber(item.providers) }}</template>
        <template #[`item.median`]="{ item }">{{ price(item.median, item) }}</template>
        <template #[`item.gap`]="{ item }">{{ item.gap == null ? 'Needs 10+ providers' : price(item.gap, item) }}</template>
      </v-data-table>
      <p v-if="lines.length > SHOWN" class="text-body-small text-medium-emphasis">
        {{ plural(lines.length - SHOWN, 'smaller group') }} in the cards below.
      </p>
      <p v-if="anyPerUnit" class="text-body-small text-medium-emphasis" data-testid="per-unit-note">
        Priced per unit; a case can use several units.
      </p>

      <div v-if="anyPerCase" class="d-flex align-center flex-wrap ga-3">
        <v-number-input
          :model-value="params.cases ?? null"
          label="Cases a year"
          :min="1"
          :max="MAX_CASES"
          control-variant="hidden"
          hide-details
          style="max-width: 180px"
          data-testid="cases-input"
          @update:model-value="onCases"
        />
        <p v-if="lead" class="text-body-medium" data-testid="yearly-gap">
          ≈ <strong>{{ wholeDollars(lead.yearly) }}</strong> a year for {{ lead.group }}: the gap per case × your
          {{ wholeNumber(params.cases) }} cases, if they went to providers at the lower quartile instead of the median.
        </p>
        <p v-else-if="params.cases" class="text-body-medium">No group here has a gap to the lower quartile to multiply.</p>
      </div>

      <ul class="text-body-small text-medium-emphasis ps-4">
        <li v-for="caveat in EMPLOYER_CAVEATS" :key="caveat">{{ caveat }}</li>
      </ul>
    </template>
  </div>
</template>
