<script setup lang="ts">
import { computed, ref } from 'vue'
import { mdiInformationOutline } from '@mdi/js'
import { money, plural } from '@/lib/format'
import {
  bandFor,
  capitalize,
  dollars,
  feeLabel,
  fewProvidersLine,
  gapLine,
  lowerFirst,
  positionLine,
  rangeLine,
  typicalPrice,
  type PriceSummary,
} from '@/lib/plainNumbers'
import type { ProviderPosition, RateGroup } from '../api/types'
import { modifierText } from '../lib/lenses'
import RangeBar from './RangeBar.vue'

const props = withDefaults(
  defineProps<{
    group: RateGroup
    selected?: boolean
    /** Over the highlights: "your provider" only when an NPI or EIN pins one; a name filter names the filter. */
    highlightHeading?: string
  }>(),
  { selected: false, highlightHeading: 'How your provider compares' },
)
const emit = defineEmits<{ showProviders: [groupKey: string] }>()

const showNotes = ref(false)

const summary = computed<PriceSummary>(() => ({ ...props.group.stats, providers: props.group.providerCount }))
const typical = computed(() => typicalPrice(summary.value))
const facilityBilling = computed(() => props.group.billedAs.filter((billed) => billed.billingCodeType === 'RC'))

/** After the copper price chip: "Lower than most · $23 less than typical". Nothing under 5 providers. */
function positionText(position: ProviderPosition): string | null {
  return positionLine(bandFor(position.rate, summary.value), position.rate, typical.value)
}
</script>

<template>
  <v-card
    :variant="selected ? 'tonal' : undefined"
    :color="selected ? 'primary' : undefined"
    class="h-100 d-flex flex-column"
    data-testid="group-card"
  >
    <v-card-item>
      <v-card-title class="text-wrap">
        {{ feeLabel(group.feeType) }} · {{ group.settingLabel }}
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
      <div v-if="group.modifiers.length || facilityBilling.length" class="text-body-medium" data-testid="group-billing">
        <p v-if="group.modifiers.length">{{ capitalize(modifierText(group.modifiers)) }}</p>
        <p v-for="billed in facilityBilling" :key="billed.billingCode">Filed under {{ lowerFirst(billed.label) }}</p>
      </div>

      <!-- Under 5 providers: just the prices, nothing to compare. -->
      <div v-if="typical == null" data-testid="stats-list">
        <p class="text-body-medium text-medium-emphasis mb-1">
          {{ fewProvidersLine(group.providerCount) }} {{ group.providerCount === 1 ? 'Its price:' : 'Their prices:' }}
        </p>
        <div class="d-flex flex-wrap ga-1">
          <v-chip v-for="(rate, index) in group.listedRates" :key="index" variant="tonal">{{ money(rate) }}</v-chip>
        </div>
      </div>

      <!-- 5 or more: the typical price and how far prices run; from 10, the lower-priced quarter. "Most providers get
           between" already says how much prices vary, so the card doesn't say it again as "about N times". -->
      <div v-else :data-testid="group.statsLevel === 'full' ? 'stats-full' : 'stats-basic'" class="d-flex flex-column ga-2">
        <div>
          <div class="text-body-small text-medium-emphasis">Typical price</div>
          <div class="text-title-large stat-figure" data-testid="typical-price">{{ dollars(typical) }}</div>
        </div>
        <p class="text-body-medium" data-testid="range-line">{{ rangeLine(summary) }}</p>
        <RangeBar :summary="summary" :marker="group.highlights[0]?.rate" :marker-label="group.highlights[0]?.displayName" />
        <v-sheet v-if="gapLine(summary)" rounded class="quartile-gap pa-3" data-testid="quartile-gap">
          <p class="text-body-medium">{{ gapLine(summary) }}</p>
        </v-sheet>
      </div>

      <div v-if="group.highlights.length" class="yours-rule d-flex flex-column ga-1" data-testid="group-highlights">
        <p class="text-body-small font-weight-medium">{{ highlightHeading }}</p>
        <div
          v-for="position in group.highlights"
          :key="position.providerKey"
          class="d-flex flex-wrap align-center ga-1 text-body-medium"
          data-testid="highlight-line"
        >
          <v-chip class="copper-mark" variant="outlined" data-testid="your-rate">{{ money(position.rate) }}</v-chip>
          <strong>{{ position.displayName }}</strong>
          <span v-if="positionText(position)" data-testid="highlight-position">{{ positionText(position) }}</span>
        </div>
      </div>

      <div v-if="group.notes.length">
        <v-btn variant="text" size="small" class="px-0" @click="showNotes = !showNotes">
          {{ showNotes ? 'Hide' : 'Show' }} notes ({{ group.notes.length }})
        </v-btn>
        <v-expand-transition>
          <ul v-show="showNotes" class="text-body-medium text-medium-emphasis ps-4 mt-1">
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
.quartile-gap {
  background: rgba(var(--v-theme-primary), 0.08);
}
</style>
