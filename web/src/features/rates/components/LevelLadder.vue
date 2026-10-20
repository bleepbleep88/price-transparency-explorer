<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { wholeNumber } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import type { RateSearchResult } from '../api/types'
import { useLevelLadder } from '../composables/useRateQueries'
import { chooseLine, ladderCaption, ladderSteps, levelLabel, lineLabel } from '../lib/levels'
import type { SearchParams } from '../lib/searchParams'

// ECharts is big: only visit-level searches download it.
const LevelLadderChart = defineAsyncComponent(() => import('./LevelLadderChart.vue'))

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()
const { smAndDown } = useDisplay()

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
  { title: 'Typical price', key: 'typical', sortable: false, align: 'end' as const },
  { title: 'How far prices run', key: 'range', sortable: false },
  { title: 'Providers', key: 'providers', sortable: false, align: 'end' as const },
]
</script>

<template>
  <v-card v-if="line && ladder.data.value" data-testid="level-ladder">
    <v-card-item>
      <v-card-title class="text-wrap">Prices by visit level</v-card-title>
      <v-card-subtitle class="text-wrap">{{ ladder.data.value.family }}: each level on the same plan, setting and fee</v-card-subtitle>
    </v-card-item>
    <v-card-text class="d-flex flex-column ga-3">
      <v-select
        v-if="lines.length > 1"
        :model-value="line.key"
        :items="lineItems"
        label="Plan, setting and fee"
        density="compact"
        hide-details
        data-testid="level-line"
        @update:model-value="(key: string) => (picked = key)"
      />
      <!-- On a phone the select cuts the label short; the setting and modifiers that tell lines apart come last. -->
      <p v-if="lines.length <= 1 || smAndDown" class="text-body-medium" data-testid="level-line-label">{{ lineLabel(line) }}</p>
      <p v-if="note" class="text-body-medium" data-testid="level-note">{{ note }}</p>

      <LevelLadderChart :steps="steps" :current="code!.code" />

      <v-data-table
        :headers="headers"
        :items="steps"
        item-value="code"
        :items-per-page="-1"
        hide-default-footer
        density="compact"
        :mobile="smAndDown"
        :row-props="({ item }) => ({ 'data-testid': 'level-step', class: item.code === code!.code ? 'font-weight-bold' : undefined })"
      >
        <template #[`item.level`]="{ item }">{{ levelLabel(item) }}</template>
        <template #[`item.typical`]="{ item }">{{ item.typical == null ? '—' : dollars(item.typical) }}</template>
        <template #[`item.range`]="{ item }">
          <span v-if="item.range">{{ item.range }}</span>
          <span v-else class="text-medium-emphasis">{{ item.text }}</span>
        </template>
        <template #[`item.providers`]="{ item }">{{ wholeNumber(item.providers) }}</template>
      </v-data-table>

      <p class="text-body-small text-medium-emphasis" data-testid="level-caption">
        {{ ladderCaption(steps) }} The files don't say how often each level is billed.
      </p>
    </v-card-text>
  </v-card>
</template>
