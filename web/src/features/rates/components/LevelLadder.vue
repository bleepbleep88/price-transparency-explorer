<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { wholeNumber } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import type { RateSearchResult } from '../api/types'
import { useLevelLadder } from '../composables/useRateQueries'
import { chooseLine, ladderSteps, levelLabel, lineLabel, lineTitle } from '../lib/levels'
import type { SearchParams } from '../lib/searchParams'

// ECharts is big: only visit-level searches download it.
const LevelLadderChart = defineAsyncComponent(() => import('./LevelLadderChart.vue'))

// Visit levels as a short bar chart of typical prices, for one plan and bill. The picker and the numbers are in Details.
const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()

const code = computed(() => props.result.code)
const ladder = useLevelLadder(
  () => (code.value?.levelFamily ? code.value.code : null),
  () => props.params.snapshot,
)

// The search's plan, setting and fee type narrow the lines, and the picked card leads.
const choice = computed(() =>
  chooseLine(ladder.data.value, code.value?.code ?? '', {
    product: props.params.product,
    setting: props.params.setting,
    feeType: props.params.feeType,
    group: props.params.group,
  }),
)
const lines = computed(() => choice.value.lines)

// The reader's pick, until the search or its picked card changes.
const picked = ref<string | null>(null)
watch(
  () => [code.value?.code, props.params.group, props.params.product, props.params.setting, props.params.feeType],
  () => (picked.value = null),
)
const line = computed(() => lines.value.find((candidate) => candidate.key === picked.value) ?? choice.value.line)
const note = computed(() => (picked.value ? null : choice.value.note))
const steps = computed(() => (ladder.data.value && line.value ? ladderSteps(ladder.data.value, line.value) : []))
const lineItems = computed(() => lines.value.map((candidate) => ({ title: lineLabel(candidate), value: candidate.key })))

const headers = [
  { title: 'Level', key: 'level', sortable: false },
  { title: 'Typical', key: 'typical', sortable: false, align: 'end' as const },
  { title: 'Most places', key: 'most', sortable: false, align: 'end' as const },
  { title: 'Places', key: 'providers', sortable: false, align: 'end' as const },
]
</script>

<template>
  <section v-if="line && ladder.data.value" class="levels" data-testid="level-ladder">
    <h2 class="section-title">Price by level</h2>
    <p class="muted" data-testid="level-line-label">{{ lineTitle(line) }}</p>

    <LevelLadderChart :steps="steps" :current="code!.code" />

    <DetailsPanel label="Details: levels" testid="level-details">
      <!-- In Details: on the main view both bills can read the same ("Doctor · in the office"); only modifiers differ. -->
      <p v-if="note" data-testid="level-note">{{ note }}</p>
      <v-select
        v-if="lines.length > 1"
        :model-value="line.key"
        :items="lineItems"
        label="Plan and bill"
        density="compact"
        hide-details
        data-testid="level-line"
        @update:model-value="(key: string) => (picked = key)"
      />
      <p>{{ lineLabel(line) }}</p>
      <v-table density="compact" class="level-table" data-testid="level-table">
        <thead>
          <tr>
            <th v-for="header in headers" :key="header.key" :class="header.align === 'end' ? 'text-end' : undefined">{{ header.title }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="step in steps" :key="step.code" data-testid="level-step" :class="{ current: step.code === code!.code }">
            <td>
              <span class="text-no-wrap">{{ levelLabel(step) }}</span>
              <span class="code">{{ step.code }}</span>
            </td>
            <td class="text-end text-no-wrap">{{ step.typical == null ? '—' : dollars(step.typical) }}</td>
            <td class="text-end text-no-wrap">{{ step.mostText ?? '—' }}</td>
            <td class="text-end">{{ wholeNumber(step.providers) }}</td>
          </tr>
        </tbody>
      </v-table>
      <p data-testid="level-caption">How often each level is billed isn't in the files.</p>
    </DetailsPanel>
  </section>
</template>

<style scoped>
.levels {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: 960px;
}

.section-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}

.muted {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.current td {
  font-weight: 600;
}

.code {
  display: block;
  font-size: 0.875rem;
  font-weight: 400;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

/* Four columns fit a phone without sideways scroll once the cell padding is halved. */
@media (max-width: 600px) {
  .level-table :deep(th),
  .level-table :deep(td) {
    padding: 0 8px !important;
  }
}
</style>
