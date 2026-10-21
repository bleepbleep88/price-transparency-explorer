<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { dollars } from '@/lib/plainNumbers'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import type { RateSearchResult } from '../api/types'
import { doseLine, dosePricing, doseRange, doseUnits, doseUnitWord, MAX_DOSE_UNITS, unitDefinition } from '../lib/dose'
import { isDrugCode } from '../lib/lenses'
import { parseDose } from '../lib/searchParams'

// A dose box and one answer: "45 mg = 5 units → $4.75". Which price it uses, and why, sits in Details. The price is
// the plan tab's (`plan`), from the tile the reader is looking at (`group`) when that tile can price a dose.
const props = defineProps<{ result: RateSearchResult; dose?: number; plan?: string | null; group?: string | null }>()
const emit = defineEmits<{ dose: [dose: number | null] }>()

const code = computed(() => props.result.code)
const unit = computed(() => (isDrugCode(code.value) ? code.value?.doseUnit : null) ?? null)
const pricing = computed(() => dosePricing(props.result, { plan: props.plan, group: props.group }))
const options = computed(() => pricing.value.options)

// The card whose price the math uses: the reader's pick in Details, else the first (the tile, else no modifiers and the
// most providers). Another code, plan or tile drops the pick.
const picked = ref<string | null>(null)
watch(
  () => [code.value?.code, props.plan, props.group],
  () => (picked.value = null),
)
const option = computed(() => options.value.find((candidate) => candidate.groupKey === picked.value) ?? options.value[0] ?? null)
const optionItems = computed(() => options.value.map((candidate) => ({ title: candidate.label, value: candidate.groupKey })))

// The dose lives in the URL (?dose=), so a shared link shows the same answer.
const doseInput = ref(props.dose != null ? String(props.dose) : '')
watch(
  () => props.dose,
  (dose) => {
    if ((dose ?? null) !== parseDose(doseInput.value)) doseInput.value = dose != null ? String(dose) : ''
  },
)

const doseError = computed(() => {
  if (!doseInput.value.trim()) return ''
  const dose = parseDose(doseInput.value)
  if (dose == null) return 'Enter a number, like 45.'
  return unit.value && doseUnits(dose, unit.value).units > MAX_DOSE_UNITS ? 'Too big: over 100,000 units.' : ''
})
const answer = computed(() => {
  const dose = parseDose(doseInput.value)
  return dose != null && unit.value && option.value ? doseLine(dose, unit.value, option.value.typical) : null
})
const range = computed(() => (answer.value && option.value ? doseRange(answer.value.units, option.value.most) : null))

// A dose the box rejects leaves the URL too, so a shared link never shows an answer the reader didn't see.
function onDoseInput(text: string) {
  doseInput.value = text
  emit('dose', doseError.value ? null : parseDose(text))
}
</script>

<template>
  <section v-if="unit && (option || pricing.reason)" class="dose" data-testid="dose-calculator">
    <h2 class="section-title">Price per dose</h2>
    <p v-if="!option" class="muted" data-testid="dose-reason">{{ pricing.reason }}</p>
    <template v-else>
      <div class="dose-row">
        <v-text-field
          :model-value="doseInput"
          label="Dose"
          :suffix="doseUnitWord(unit)"
          inputmode="decimal"
          autocomplete="off"
          :error-messages="doseError"
          density="compact"
          hide-details="auto"
          class="dose-input"
          data-testid="dose-input"
          @update:model-value="onDoseInput"
        />
        <p aria-live="polite" class="dose-answer" data-testid="dose-answer">
          <template v-if="answer">{{ answer.text }}</template>
          <span v-else-if="!doseError" class="muted">{{ unitDefinition(unit) ?? 'Enter a dose' }}</span>
        </p>
      </div>
      <p v-if="answer && unitDefinition(unit)" class="muted" data-testid="dose-unit">{{ unitDefinition(unit) }}</p>

      <DetailsPanel label="Details: dose" testid="dose-details">
        <v-select
          v-if="options.length > 1"
          :model-value="option.groupKey"
          :items="optionItems"
          label="Price from"
          density="compact"
          hide-details
          data-testid="dose-card"
          @update:model-value="(key: string) => (picked = key)"
        />
        <p data-testid="dose-card-label">Price from: {{ option.label }}</p>
        <p>Typical price: {{ dollars(option.typical) }} a unit</p>
        <p v-if="range">{{ range }}</p>
        <p>The drug only. Giving it is billed separately.</p>
        <p>Thrown-away drug is billed on its own line (modifier JW).</p>
      </DetailsPanel>
    </template>
  </section>
</template>

<style scoped>
.dose {
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

.dose-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.dose-input {
  flex: 0 0 160px;
}

.dose-answer {
  font-size: 1rem;
  font-weight: 600;
}

.muted {
  font-size: 0.875rem;
  font-weight: 400;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}
</style>
