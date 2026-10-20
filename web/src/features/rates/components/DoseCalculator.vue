<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { wholeNumber } from '@/lib/format'
import type { RateSearchResult } from '../api/types'
import { doseMath, dosePricing, doseUnits, doseUnitWord, MAX_DOSE_UNITS } from '../lib/dose'
import { isDrugCode } from '../lib/lenses'
import { parseDose } from '../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; dose?: number }>()
const emit = defineEmits<{ dose: [dose: number | null] }>()
const { smAndDown } = useDisplay()

const code = computed(() => props.result.code)
const unit = computed(() => (isDrugCode(code.value) ? code.value?.doseUnit : null) ?? null)
const pricing = computed(() => dosePricing(props.result))
const options = computed(() => pricing.value.options)

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
    if ((dose ?? null) !== parseDose(doseInput.value)) doseInput.value = dose != null ? String(dose) : ''
  },
)

const tooManyUnits = computed(() => {
  const dose = parseDose(doseInput.value)
  return dose != null && unit.value != null && doseUnits(dose, unit.value).units > MAX_DOSE_UNITS
})
const doseError = computed(() => {
  if (!doseInput.value.trim()) return ''
  if (parseDose(doseInput.value) == null) return 'Enter a dose as a plain number, like 45 or 2.5.'
  return tooManyUnits.value ? `That's more than ${wholeNumber(MAX_DOSE_UNITS)} units: enter a smaller dose.` : ''
})
const math = computed(() => {
  const dose = parseDose(doseInput.value)
  return dose != null && unit.value && option.value ? doseMath(dose, unit.value, option.value.typical, option.value.most) : null
})

// A dose the box rejects leaves the URL too, so a shared link never shows math the reader didn't see.
function onDoseInput(text: string) {
  doseInput.value = text
  emit('dose', doseError.value ? null : parseDose(text))
}
</script>

<template>
  <v-card v-if="unit && (option || pricing.reason)" data-testid="dose-calculator">
    <v-card-item>
      <v-card-title class="text-wrap">What a dose comes to at the plan's rates</v-card-title>
      <v-card-subtitle class="text-wrap">One unit of {{ code!.code }} is {{ unit.text }}, as the code's name says.</v-card-subtitle>
    </v-card-item>
    <v-card-text class="d-flex flex-column ga-3">
      <p v-if="!option" class="text-body-medium" data-testid="dose-reason">{{ pricing.reason }}</p>
      <template v-else>
        <div class="d-flex flex-wrap ga-3">
          <v-text-field
            :model-value="doseInput"
            :label="`Dose (${doseUnitWord(unit)})`"
            :suffix="doseUnitWord(unit)"
            inputmode="decimal"
            autocomplete="off"
            :error-messages="doseError"
            density="compact"
            class="dose-input"
            data-testid="dose-input"
            @update:model-value="onDoseInput"
          />
          <div v-if="options.length > 1" class="flex-grow-1 dose-card">
            <v-select
              :model-value="option.groupKey"
              :items="optionItems"
              label="Price from"
              density="compact"
              hide-details
              data-testid="dose-card"
              @update:model-value="(key: string) => (picked = key)"
            />
            <!-- On a phone the select cuts the label short; the part that tells cards apart comes last. -->
            <p v-if="smAndDown" class="text-body-small mt-1" data-testid="dose-card-label">{{ option.label }}</p>
          </div>
          <p v-else class="text-body-medium align-self-center">Price from: {{ option.label }}</p>
        </div>

        <div aria-live="polite" class="d-flex flex-column ga-1" data-testid="dose-math-region">
          <template v-if="math">
            <div class="d-flex flex-column ga-1" data-testid="dose-math">
              <p v-for="line in math.lines" :key="line" class="text-body-medium">{{ line }}</p>
            </div>
          </template>
          <p v-else-if="!doseError" class="text-body-medium text-medium-emphasis">Enter a dose to see how many units it is and what it comes to.</p>
        </div>
      </template>

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

.dose-card {
  min-width: 0;
}
</style>
