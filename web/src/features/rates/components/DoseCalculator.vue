<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { RateSearchResult } from '../api/types'
import { doseMath, dosePriceOptions } from '../lib/dose'
import { isDrugCode } from '../lib/lenses'
import { MAX_DOSE } from '../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; dose?: number }>()
const emit = defineEmits<{ dose: [dose: number | null] }>()

const code = computed(() => props.result.code)
const unit = computed(() => (isDrugCode(code.value) ? code.value?.doseUnit : null) ?? null)
const options = computed(() => dosePriceOptions(props.result))

// The card whose price the math uses: the reader's pick, else the first (no modifiers, most providers).
const picked = ref<string | null>(null)
watch(
  () => code.value?.code,
  () => (picked.value = null),
)
const option = computed(() => options.value.find((candidate) => candidate.groupKey === picked.value) ?? options.value[0] ?? null)
const optionItems = computed(() => options.value.map((candidate) => ({ title: candidate.label, value: candidate.groupKey })))

// The dose lives in the URL (?dose=), so a shared link shows the same math.
const doseInput = ref(props.dose != null ? String(props.dose) : '')
watch(
  () => props.dose,
  (dose) => {
    if (dose !== parseDose(doseInput.value)) doseInput.value = dose != null ? String(dose) : ''
  },
)

function parseDose(text: string): number | null {
  const value = Number(text.trim())
  return text.trim() && Number.isFinite(value) && value > 0 && value <= MAX_DOSE ? value : null
}

const doseError = computed(() => (doseInput.value.trim() && parseDose(doseInput.value) == null ? `Enter a dose above 0, up to ${MAX_DOSE.toLocaleString('en-US')}.` : ''))
const math = computed(() => {
  const dose = parseDose(doseInput.value)
  return dose != null && unit.value && option.value ? doseMath(dose, unit.value, option.value.typical, option.value.most) : null
})

function onDoseInput(text: string) {
  doseInput.value = text
  const dose = parseDose(text)
  if (dose != null || !text.trim()) emit('dose', dose)
}
</script>

<template>
  <v-card v-if="unit && option" data-testid="dose-calculator">
    <v-card-item>
      <v-card-title class="text-wrap">What a dose costs</v-card-title>
      <v-card-subtitle class="text-wrap">One unit of {{ code!.code }} is {{ unit.text }}, as the code's name says.</v-card-subtitle>
    </v-card-item>
    <v-card-text class="d-flex flex-column ga-3">
      <div class="d-flex flex-wrap ga-3">
        <v-text-field
          :model-value="doseInput"
          :label="`Dose (${unit.unit})`"
          :suffix="unit.unit"
          inputmode="decimal"
          autocomplete="off"
          :error-messages="doseError"
          density="compact"
          class="dose-input"
          data-testid="dose-input"
          @update:model-value="onDoseInput"
        />
        <v-select
          v-if="options.length > 1"
          :model-value="option.groupKey"
          :items="optionItems"
          label="Price from"
          density="compact"
          hide-details
          class="flex-grow-1"
          data-testid="dose-card"
          @update:model-value="(key: string) => (picked = key)"
        />
        <p v-else class="text-body-medium align-self-center">Price from: {{ option.label }}</p>
      </div>

      <div v-if="math" class="d-flex flex-column ga-1" data-testid="dose-math">
        <p v-for="line in math.lines" :key="line" class="text-body-medium">{{ line }}</p>
      </div>
      <p v-else class="text-body-medium text-medium-emphasis">Enter a dose to see how many units it is and what it costs.</p>

      <p class="text-body-small text-medium-emphasis" data-testid="dose-notes">
        This is the drug alone: giving it, the injection or infusion, is billed under its own code. When part of a
        single-dose vial is thrown away, the discarded amount can be billed on its own line with modifier JW.
      </p>
    </v-card-text>
  </v-card>
</template>

<style scoped>
.dose-input {
  max-width: 220px;
}
</style>
