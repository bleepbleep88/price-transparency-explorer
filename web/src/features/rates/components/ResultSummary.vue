<script setup lang="ts">
import { computed } from 'vue'
import { mdiInformationOutline } from '@mdi/js'
import { plural, shortDate, wholeNumber } from '@/lib/format'
import type { LocationOption, ProductFileSummary, RateSearchResult, SettingOption } from '../api/types'
import { groupName } from '../lib/lenses'
import { locationTitle } from '../lib/locations'
import { pricingNotes } from '../lib/pricing'
import type { QueryPatch, SearchParams } from '../lib/searchParams'

const props = defineProps<{
  result: RateSearchResult
  params: SearchParams
  products: ProductFileSummary[]
  settings: SettingOption[]
  locations?: LocationOption[]
}>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const code = computed(() => props.result.code)
// "Procedures · Digestive/Gastrointestinal"; CMS's family, when there is one, in the tooltip.
const categoryLabel = computed(() => {
  const classification = code.value?.classification
  if (!classification) return null
  return classification.subcategory ? `${classification.category} · ${classification.subcategory}` : classification.category
})
const notes = computed(() => pricingNotes(props.result))

// Removable chips for everything that narrows the results.
const activeFilters = computed(() => {
  const p = props.params
  // yours: the chip names the provider being looked up, so it carries the copper mark.
  const chips: { key: keyof SearchParams; label: string; yours?: boolean }[] = []
  if (p.codeType) chips.push({ key: 'codeType', label: `Code type: ${p.codeType}` })
  if (p.npi) chips.push({ key: 'npi', label: `NPI ${p.npi}`, yours: true })
  if (p.ein) chips.push({ key: 'ein', label: p.ein.startsWith('u') ? 'One provider (masked tax ID)' : `EIN ${p.ein}`, yours: true })
  if (p.provider) chips.push({ key: 'provider', label: `Name contains "${p.provider}"`, yours: true })
  // Specialty and location pick a set of providers, not "your" provider: no copper mark.
  if (p.specialty) chips.push({ key: 'specialty', label: `Specialty: ${p.specialty}` })
  if (p.county) {
    // The place the API matched ("Brooklyn" for "Kings County"), else the list's name for it.
    const matched = props.result.query.county === p.county ? props.result.query.countyPlace : null
    chips.push({ key: 'county', label: `Location: ${matched ?? locationTitle(p.county, props.locations ?? [])}` })
  }
  if (p.product) chips.push({ key: 'product', label: props.products.find((x) => x.productKey === p.product)?.productLabel ?? p.product })
  if (p.setting) chips.push({ key: 'setting', label: props.settings.find((x) => x.key === p.setting)?.label ?? p.setting })
  if (p.feeType) chips.push({ key: 'feeType', label: p.feeType === 'facility' ? 'Facility fees' : "Doctor's fees" })
  if (p.group) {
    const picked = props.result.groups.find((group) => group.key === p.group)
    chips.push({ key: 'group', label: picked ? `Only ${groupName(picked)}` : 'One card' })
  }
  return chips
})

const qualityChips = computed(() => {
  const q = props.result.quality
  const chips: { text: string; tip: string }[] = []
  if (q.duplicatesCollapsed > 0) {
    chips.push({
      text: `${wholeNumber(q.publishedRows)} published rows → ${plural(q.uniqueRates, 'unique rate')}`,
      tip: 'The plan publishes the same price many times. Identical rows are counted once.',
    })
  }
  if (q.providersWithMultipleRates > 0) {
    chips.push({
      text: `${plural(q.providersWithMultipleRates, 'provider')} with more than one rate`,
      tip: 'Same provider, code and setting but different prices. Shown as a range; comparisons use the price in the Price column.',
    })
  }
  if (q.maskedTaxIds > 0) {
    chips.push({
      text: `${plural(q.maskedTaxIds, 'masked tax ID')}`,
      tip: 'Tax IDs with a prefix the IRS never issues may be Social Security numbers, so they are masked.',
    })
  }
  if (q.wholeTaxIdContracts > 0) {
    chips.push({
      text: `${plural(q.wholeTaxIdContracts, 'whole-tax-ID contract')}`,
      tip: 'The plan lists no individual NPIs: the rate applies to anyone billing under that tax ID.',
    })
  }
  return chips
})
</script>

<template>
  <section class="d-flex flex-column ga-3" aria-labelledby="result-heading">
    <div class="d-flex flex-wrap align-center ga-2">
      <h2 id="result-heading" class="text-headline-small">
        {{ code ? `${code.codeType} ${code.code}` : result.query.code }}
      </h2>
      <span v-if="code" class="text-title-large font-weight-regular text-medium-emphasis" data-testid="code-name">
        {{ code.name ?? 'No description published by the plan' }}
      </span>
      <v-tooltip
        v-if="code?.descriptionSource === 'cms_hcpcs'"
        :text="code.description ?? code.name ?? ''"
        :disabled="!code.description"
        max-width="480"
        location="bottom"
      >
        <template #activator="{ props: tip }">
          <span v-bind="tip" class="text-body-small text-medium-emphasis" :class="{ 'has-tooltip': code.description }" :tabindex="code.description ? 0 : undefined" data-testid="cms-name">
            Name from CMS's HCPCS list: the plan's file doesn't name this code.
          </span>
        </template>
      </v-tooltip>
    </div>

    <div v-if="code" class="d-flex flex-wrap align-center ga-2 text-body-medium">
      <v-tooltip v-if="code.classification.family" :text="`CMS's category for this code: ${code.classification.family}`" max-width="360" location="bottom">
        <template #activator="{ props: tip }">
          <v-chip v-bind="tip" variant="tonal" color="primary" data-testid="category-chip">{{ categoryLabel }}</v-chip>
        </template>
      </v-tooltip>
      <v-chip v-else variant="tonal" color="primary" data-testid="category-chip">{{ categoryLabel }}</v-chip>
      <span>Priced {{ code.category.unit }}</span>
      <span class="text-medium-emphasis">
        · Found as: {{ code.foundVia.slice(0, 2).join('; ') }}
        <v-tooltip v-if="code.foundVia.length > 2" :text="code.foundVia.slice(2).join('; ')" max-width="420" location="bottom">
          <template #activator="{ props: tip }">
            <span v-bind="tip" class="has-tooltip" tabindex="0">and {{ code.foundVia.length - 2 }} more</span>
          </template>
        </v-tooltip>
      </span>
      <span class="text-medium-emphasis">· Snapshot {{ shortDate(result.snapshot.indexDate) }}</span>
    </div>
    <p v-if="code?.levelFamily" class="text-body-medium text-medium-emphasis">
      {{ code.levelFamily }} codes come in levels. Compare level to level.
    </p>

    <v-expansion-panels v-if="notes.length" variant="accordion" flat class="pricing-notes" data-testid="pricing-notes">
      <v-expansion-panel>
        <v-expansion-panel-title class="text-body-medium">How it's priced</v-expansion-panel-title>
        <v-expansion-panel-text>
          <dl class="d-flex flex-column ga-2">
            <div v-for="note in notes" :key="note.key" :data-testid="`pricing-note-${note.key}`">
              <dt class="text-body-medium font-weight-medium">{{ note.title }}</dt>
              <dd class="text-body-medium">{{ note.text }}</dd>
            </div>
          </dl>
        </v-expansion-panel-text>
      </v-expansion-panel>
    </v-expansion-panels>

    <div v-if="activeFilters.length" class="d-flex flex-wrap ga-2" aria-label="Active filters">
      <v-chip
        v-for="chip in activeFilters"
        :key="chip.key"
        closable
        :close-label="`Remove ${chip.label}`"
        class="active-filter"
        :class="{ 'copper-mark': chip.yours }"
        :variant="chip.yours ? 'outlined' : undefined"
        data-testid="active-filter"
        @click:close="emit('update', { [chip.key]: null })"
      >
        {{ chip.label }}
      </v-chip>
    </div>

    <div v-if="qualityChips.length" class="d-flex flex-wrap ga-2" aria-label="Data clean-up">
      <v-tooltip v-for="chip in qualityChips" :key="chip.text" :text="chip.tip" max-width="320" location="top">
        <template #activator="{ props: tip }">
          <v-chip v-bind="tip" variant="outlined" :prepend-icon="mdiInformationOutline" size="small">{{ chip.text }}</v-chip>
        </template>
      </v-tooltip>
    </div>

    <v-alert
      v-for="(hint, index) in result.hints"
      :key="`${hint.kind}-${index}`"
      :type="hint.severity === 'warning' ? 'warning' : 'info'"
      variant="tonal"
      density="comfortable"
      :data-testid="`hint-${hint.kind}`"
    >
      <p>{{ hint.message }}</p>
      <div v-if="hint.links.length" class="d-flex flex-wrap ga-2 mt-2">
        <v-btn
          v-for="link in hint.links"
          :key="link.label"
          size="small"
          variant="outlined"
          @click="emit('update', { ...link.query, group: null })"
        >
          {{ link.label }}
        </v-btn>
      </div>
    </v-alert>
  </section>
</template>

<style scoped>
/* A long specialty name wraps inside its chip, so the remove button stays on screen on a phone. */
.active-filter {
  height: auto;
  min-height: var(--v-chip-height);
  white-space: normal;
  padding-block: 4px;
}

.active-filter :deep(.v-chip__content) {
  min-width: 0;
}

.pricing-notes {
  max-width: 760px;
}
</style>
