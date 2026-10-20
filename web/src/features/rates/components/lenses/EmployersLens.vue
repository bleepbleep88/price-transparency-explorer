<script setup lang="ts">
import { computed } from 'vue'
import { money, plural, wholeNumber } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { costLines, EMPLOYER_CAVEATS } from '../../lib/lenses'
import { MAX_CASES, type SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()
const emit = defineEmits<{ cases: [cases: number | null] }>()

const SHOWN = 8
const lines = computed(() => costLines(props.result, props.params.cases))
// The sentence speaks for the largest group with a gap above zero (a zero gap has nothing to multiply);
// the table shows every group's figure.
const lead = computed(() => lines.value.find((line) => line.yearly != null && line.gap != null && line.gap > 0) ?? null)

function onCases(value: number | string | null) {
  const cases = Number(value)
  emit('cases', Number.isInteger(cases) && cases >= 1 && cases <= MAX_CASES ? cases : null)
}
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-employers">
    <p v-if="!lines.length" class="text-body-medium">No group here has enough providers for a typical cost.</p>
    <template v-else>
      <v-table density="compact">
        <thead>
          <tr>
            <th>Comparison group</th>
            <th class="text-end">Providers</th>
            <th class="text-end">Typical (median)</th>
            <th class="text-end">Gap per case</th>
            <th v-if="params.cases" class="text-end">≈ A year</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="line in lines.slice(0, SHOWN)" :key="line.groupKey" data-testid="cost-line">
            <td>{{ line.group }}</td>
            <td class="text-end">{{ wholeNumber(line.providers) }}</td>
            <td class="text-end">{{ money(line.median) }}</td>
            <td class="text-end">{{ line.gap == null ? 'Needs 10+ providers' : money(line.gap) }}</td>
            <td v-if="params.cases" class="text-end">{{ line.yearly == null ? '—' : money(line.yearly) }}</td>
          </tr>
        </tbody>
      </v-table>
      <p v-if="lines.length > SHOWN" class="text-body-small text-medium-emphasis">
        {{ plural(lines.length - SHOWN, 'smaller group') }} in the cards below.
      </p>

      <div class="d-flex align-center flex-wrap ga-3">
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
          ≈ <strong>{{ money(lead.yearly) }}</strong> a year if those {{ wholeNumber(params.cases) }} cases went to
          lower-quartile-priced providers ({{ lead.group }}).
        </p>
        <p v-else-if="params.cases" class="text-body-medium">No group here has a gap to the lower quartile to multiply.</p>
      </div>

      <ul class="text-body-small text-medium-emphasis ps-4">
        <li v-for="caveat in EMPLOYER_CAVEATS" :key="caveat">{{ caveat }}</li>
      </ul>
    </template>
  </div>
</template>
