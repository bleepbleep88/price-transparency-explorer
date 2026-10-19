<script setup lang="ts">
import { computed, ref } from 'vue'
import { mdiInformationOutline } from '@mdi/js'
import { money, ordinal, plural, times } from '@/lib/format'
import type { ProviderPosition, RateGroup } from '../api/types'
import RangeBar from './RangeBar.vue'

const props = defineProps<{ group: RateGroup; selected?: boolean }>()
const emit = defineEmits<{ showProviders: [groupKey: string] }>()

const annualCases = ref<number | null>(null)
const showNotes = ref(false)

const stats = computed(() => props.group.stats)
const annualSavings = computed(() =>
  props.group.savingsPerCase != null && annualCases.value ? props.group.savingsPerCase * annualCases.value : null,
)
const facilityBilling = computed(() => props.group.billedAs.filter((billed) => billed.billingCodeType === 'RC'))

function positionText(position: ProviderPosition): string {
  const parts = [money(position.rate)]
  if (position.percentile != null) parts.push(`${ordinal(position.percentile)} percentile`)
  if (position.vsMedian != null) parts.push(`${times(position.vsMedian)} median`)
  if (position.aboveP25 != null) {
    parts.push(
      position.aboveP25 === 0
        ? 'at the 25th percentile'
        : `${money(Math.abs(position.aboveP25))} ${position.aboveP25 > 0 ? 'above' : 'below'} the 25th percentile`,
    )
  }
  return parts.join(' · ')
}
</script>

<template>
  <v-card
    :variant="selected ? 'tonal' : 'outlined'"
    :color="selected ? 'primary' : undefined"
    class="h-100 d-flex flex-column"
    data-testid="group-card"
  >
    <v-card-item>
      <v-card-title class="text-subtitle-1 font-weight-medium text-wrap">
        {{ group.feeTypeLabel }} · {{ group.settingLabel }}
      </v-card-title>
      <v-card-subtitle class="text-wrap">
        {{ group.productLabel }} · {{ plural(group.providerCount, 'provider') }} · {{ group.unit }}
      </v-card-subtitle>
      <template v-if="group.settingNote" #append>
        <v-tooltip :text="group.settingNote" max-width="320" location="top">
          <template #activator="{ props: tip }">
            <v-icon v-bind="tip" :icon="mdiInformationOutline" size="small" aria-label="About this setting" />
          </template>
        </v-tooltip>
      </template>
    </v-card-item>

    <v-card-text class="flex-grow-1 d-flex flex-column ga-3">
      <div v-if="group.modifiers.length || facilityBilling.length" class="d-flex flex-wrap ga-1">
        <v-tooltip v-for="modifier in group.modifiers" :key="modifier.code" :text="modifier.label" location="top">
          <template #activator="{ props: tip }">
            <v-chip v-bind="tip" variant="outlined" label>Modifier {{ modifier.code }}</v-chip>
          </template>
        </v-tooltip>
        <v-chip v-for="billed in facilityBilling" :key="billed.billingCode" variant="outlined" label>{{ billed.label }}</v-chip>
      </div>

      <!-- Fewer than 5 providers: list the rates, no statistics. -->
      <div v-if="group.statsLevel === 'list'" data-testid="stats-list">
        <p class="text-body-2 text-medium-emphasis mb-1">Too few providers for statistics. Rates:</p>
        <div class="d-flex flex-wrap ga-1">
          <v-chip v-for="(rate, index) in group.listedRates" :key="index" variant="tonal">{{ money(rate) }}</v-chip>
        </div>
      </div>

      <!-- 5 to 9 providers: median and range. -->
      <div v-else-if="group.statsLevel === 'basic'" data-testid="stats-basic">
        <p class="text-h6">{{ money(stats.median) }} <span class="text-body-2 text-medium-emphasis">median</span></p>
        <p class="text-body-2 text-medium-emphasis">Range {{ money(stats.min) }} to {{ money(stats.max) }}</p>
        <RangeBar :stats="stats" :marker="group.highlights[0]?.rate" class="mt-1" />
      </div>

      <!-- 10+ providers: full statistics and savings. -->
      <div v-else data-testid="stats-full" class="d-flex flex-column ga-2">
        <div class="d-flex justify-space-between text-center">
          <div>
            <div class="text-caption text-medium-emphasis">25th percentile</div>
            <div class="text-subtitle-1">{{ money(stats.p25) }}</div>
          </div>
          <div>
            <div class="text-caption text-medium-emphasis">Median</div>
            <div class="text-h6">{{ money(stats.median) }}</div>
          </div>
          <div>
            <div class="text-caption text-medium-emphasis">75th percentile</div>
            <div class="text-subtitle-1">{{ money(stats.p75) }}</div>
          </div>
        </div>
        <RangeBar :stats="stats" :marker="group.highlights[0]?.rate" />
        <p class="text-body-2 text-medium-emphasis">
          Highest is {{ times(stats.spreadMultiple) }} the lowest<template v-if="stats.p90ToP10 != null">
            · 90th to 10th percentile: {{ times(stats.p90ToP10) }}</template
          >
        </p>
        <p v-if="group.savingsPerCase === 0" class="text-body-2 text-medium-emphasis" data-testid="no-savings">
          No saving from switching providers: the 25th percentile and the median are the same rate.
        </p>
        <v-sheet v-else-if="group.savingsPerCase != null" rounded="lg" class="savings pa-3 d-flex flex-column ga-2" data-testid="savings">
          <p class="text-body-2">
            <strong>{{ money(group.savingsPerCase) }} per case</strong> saved by moving care from a typical (median)
            provider to one at the 25th percentile.
          </p>
          <div class="d-flex align-center ga-3 flex-wrap">
            <v-number-input
              v-model="annualCases"
              label="Cases a year"
              :min="0"
              :max="1000000"
              control-variant="hidden"
              density="compact"
              hide-details
              style="max-width: 150px"
              data-testid="annual-cases"
            />
            <span v-if="annualSavings != null" class="text-body-2" data-testid="annual-savings">
              ≈ <strong>{{ money(annualSavings) }}</strong> a year
            </span>
          </div>
        </v-sheet>
      </div>

      <div v-if="group.highlights.length" class="d-flex flex-column ga-1" data-testid="group-highlights">
        <p class="text-caption font-weight-medium">Where your provider sits</p>
        <p v-for="position in group.highlights" :key="position.providerKey" class="text-body-2">
          <strong>{{ position.displayName }}</strong>: {{ positionText(position) }}
        </p>
      </div>

      <div v-if="group.notes.length">
        <v-btn variant="text" size="small" class="px-0" @click="showNotes = !showNotes">
          {{ showNotes ? 'Hide' : 'Show' }} notes ({{ group.notes.length }})
        </v-btn>
        <v-expand-transition>
          <ul v-show="showNotes" class="text-body-2 text-medium-emphasis ps-4 mt-1">
            <li v-for="(note, index) in group.notes" :key="index">{{ note }}</li>
          </ul>
        </v-expand-transition>
      </div>
    </v-card-text>

    <v-card-actions>
      <v-btn variant="text" color="primary" :disabled="selected" @click="emit('showProviders', group.key)">
        {{ selected ? 'Showing these providers' : 'Show these providers' }}
      </v-btn>
    </v-card-actions>
  </v-card>
</template>

<style scoped>
.savings {
  background: rgba(var(--v-theme-primary), 0.08);
}
</style>
