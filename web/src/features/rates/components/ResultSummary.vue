<script setup lang="ts">
import { computed } from 'vue'
import { mdiInformationOutline } from '@mdi/js'
import { plural, shortDate, wholeNumber } from '@/lib/format'
import type { ProductFileSummary, RateSearchResult, SettingOption } from '../api/types'
import type { QueryPatch, SearchParams } from '../lib/searchParams'

const props = defineProps<{
  result: RateSearchResult
  params: SearchParams
  products: ProductFileSummary[]
  settings: SettingOption[]
}>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const code = computed(() => props.result.code)

// Removable chips for everything that narrows the results.
const activeFilters = computed(() => {
  const p = props.params
  // yours: the chip names the provider being looked up, so it carries the copper mark.
  const chips: { key: keyof SearchParams; label: string; yours?: boolean }[] = []
  if (p.codeType) chips.push({ key: 'codeType', label: `Code type: ${p.codeType}` })
  if (p.npi) chips.push({ key: 'npi', label: `NPI ${p.npi}`, yours: true })
  if (p.ein) chips.push({ key: 'ein', label: p.ein.startsWith('u') ? 'One provider (masked tax ID)' : `EIN ${p.ein}`, yours: true })
  if (p.provider) chips.push({ key: 'provider', label: `Name contains "${p.provider}"`, yours: true })
  if (p.product) chips.push({ key: 'product', label: props.products.find((x) => x.productKey === p.product)?.productLabel ?? p.product })
  if (p.setting) chips.push({ key: 'setting', label: props.settings.find((x) => x.key === p.setting)?.label ?? p.setting })
  if (p.feeType) chips.push({ key: 'feeType', label: p.feeType === 'facility' ? 'Facility fees' : 'Professional fees' })
  if (p.group) chips.push({ key: 'group', label: 'One comparison group' })
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
      tip: 'Same provider, code and setting but different prices. Shown as a range; stats use the middle value.',
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
      <span v-if="code" class="text-title-large font-weight-regular text-medium-emphasis">
        {{ code.name ?? 'No description published by the plan' }}
      </span>
    </div>

    <div v-if="code" class="d-flex flex-wrap align-center ga-2 text-body-medium">
      <v-chip variant="tonal" color="primary">{{ code.category.label }}</v-chip>
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
      {{ code.levelFamily }} codes come in levels. Compare level to level; how often each level is billed isn't in these files.
    </p>

    <div v-if="activeFilters.length" class="d-flex flex-wrap ga-2" aria-label="Active filters">
      <v-chip
        v-for="chip in activeFilters"
        :key="chip.key"
        closable
        :close-label="`Remove ${chip.label}`"
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
