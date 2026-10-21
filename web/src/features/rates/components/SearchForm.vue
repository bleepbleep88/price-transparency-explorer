<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { mdiMagnify } from '@mdi/js'
import { plural } from '@/lib/format'
import { looksLikeCode, plainNameMatches, providerSuggestionLine, serviceTitle, settingName } from '@/lib/plainNames'
import type {
  CodeSuggestion,
  LocationOption,
  ProductFileSummary,
  ProviderSuggestion,
  SettingOption,
  SpecialtySuggestion,
} from '../api/types'
import { useCodeSuggestions, useProviderSuggestions, useSpecialtySuggestions } from '../composables/useRateQueries'
import { findLocation, locationSelectItems } from '../lib/locations'
import {
  npiCheckDigitWarning,
  validateSearchForm,
  type FeeType,
  type FormErrors,
  type FormField,
  type QueryPatch,
  type SearchParams,
} from '../lib/searchParams'

const props = defineProps<{
  params: SearchParams
  products: ProductFileSummary[]
  settings: SettingOption[]
  /** New York counties and NYC boroughs for the location filter (empty without a provider directory). */
  locations?: LocationOption[]
  /** Errors from the URL (a pasted link) or the server (400 field errors). */
  externalErrors?: Partial<Record<string, string[] | string>>
  busy?: boolean
}>()
const emit = defineEmits<{ submit: [patch: QueryPatch]; clear: [] }>()

// Comboboxes hold null when empty: Vuetify treats '' as a chosen value and hides the placeholder.
const draft = reactive({
  code: null as string | null,
  npi: '',
  ein: '',
  provider: null as string | null,
  product: null as string | null,
  setting: null as string | null,
  feeType: null as FeeType | null,
  specialty: null as string | null,
  county: null as string | null,
})
const localErrors = ref<FormErrors>({})
const codeSearch = ref('')
const providerSearch = ref('')
const specialtySearch = ref('')
const countySearch = ref('')
const filtersOpen = ref<number[]>([])

function syncFromParams(params: SearchParams) {
  draft.code = params.code || null
  draft.npi = params.npi ?? ''
  draft.ein = params.ein ?? ''
  draft.provider = params.provider || null
  draft.product = params.product ?? null
  draft.setting = params.setting ?? null
  draft.feeType = params.feeType ?? null
  draft.specialty = params.specialty || null
  draft.county = params.county || null
  localErrors.value = {}
  const anyFilter =
    params.npi || params.ein || params.product || params.setting || params.feeType || params.provider || params.specialty || params.county
  filtersOpen.value = anyFilter ? [0] : filtersOpen.value
}
// Only a different search re-syncs the form. Each field is its own source, so lens, cases, paging, sorting and
// group picks (which also change the URL) leave unsearched edits and a collapsed filter panel alone.
watch(
  [
    () => props.params.code,
    () => props.params.codeType,
    () => props.params.npi,
    () => props.params.ein,
    () => props.params.provider,
    () => props.params.product,
    () => props.params.setting,
    () => props.params.feeType,
    () => props.params.specialty,
    () => props.params.county,
  ],
  () => syncFromParams(props.params),
  { immediate: true },
)

const codeSuggestions = useCodeSuggestions(codeSearch, () => props.params.snapshot)
const providerSuggestions = useProviderSuggestions(providerSearch, () => props.params.snapshot)
// The plan's codes for what's typed, with the plain names people use ("botox", "knee mri") first.
const codeItems = computed<CodeSuggestion[]>(() => {
  const server = codeSuggestions.data.value ?? []
  const plain = looksLikeCode(codeSearch.value) ? [] : plainNameMatches(codeSearch.value)
  const local = plain
    .filter((match) => !server.some((item) => item.code === match.code))
    .map((match) => ({ codeType: '', code: match.code, name: match.name, description: null, category: '', unit: '', billedDirectly: true, facilityRevenueCodes: [], rates: 0 }))
  const ranked = [...server].sort((a, b) => rank(a) - rank(b))
  return [...local, ...ranked]
})

/** Server suggestions: the plain name typed first ("colonoscopy" → 45378), then the API's order. */
function rank(item: CodeSuggestion): number {
  const typed = codeSearch.value.trim().toLowerCase()
  const title = serviceTitle(item.code, item.name).toLowerCase()
  return title === typed ? 0 : title.startsWith(typed) ? 1 : 2
}
const providerItems = computed<ProviderSuggestion[]>(() => providerSuggestions.data.value ?? [])
const specialtySuggestions = useSpecialtySuggestions(specialtySearch)
const specialtyItems = computed<SpecialtySuggestion[]>(() => specialtySuggestions.data.value ?? [])
const locationItems = computed(() => locationSelectItems(props.locations ?? []))

// Before a search only: once searched, the page's hint says it, so it isn't said twice.
const npiWarning = computed(() => npiCheckDigitWarning(draft.npi))
const feeTypes = [
  { title: "The place's bill", value: 'facility' },
  { title: "The doctor's bill", value: 'professional' },
]
const productItems = computed(() => props.products.map((product) => ({ title: product.productLabel, value: product.productKey })))
// Plain place names; the two video-visit settings keep their difference.
const VIDEO: Record<string, string> = { 'telehealth-home': 'Video visit, at home', 'telehealth-other': 'Video visit, not at home' }
const settingItems = computed(() =>
  props.settings.map((setting) => ({ title: VIDEO[setting.key] ?? settingName(setting.key, setting.label), value: setting.key })),
)
const activeFilterCount = computed(
  () => [draft.npi, draft.ein, draft.provider, draft.product, draft.setting, draft.feeType, draft.specialty, draft.county].filter(Boolean).length,
)

function errorFor(field: FormField): string[] {
  const local = localErrors.value[field]
  if (local) return [local]
  const external = props.externalErrors?.[field]
  if (!external) return []
  return Array.isArray(external) ? external : [external]
}

/** A suggestion's second line: the code, and how many prices the plan lists for it when known. */
function describeCode(item: CodeSuggestion): string {
  return item.rates ? `${item.code} · ${plural(item.rates, 'price')}` : item.code
}

// An autocomplete forgets text that wasn't picked when it loses focus (a click on Search). Keep a place typed in full,
// or by its county's name ("Kings County").
function keepTypedLocation() {
  const match = findLocation(countySearch.value, props.locations ?? [])
  if (match && draft.county !== match.label) draft.county = match.label
}

// Vuetify's combobox keeps Enter for itself (to pick a suggestion), so the form never submits. Search once it's done.
function onComboboxEnter(event: KeyboardEvent) {
  if (event.defaultPrevented && !event.isComposing) void nextTick(onSubmit)
}

/**
 * What to search for what's in the box. Something that looks like a code (45378, J0585) is a code. Words search by
 * name: a plain name people use ("botox", "knee mri") first, then the plan's names, the closest first ("colonoscopy" →
 * 45378, not 45380). Words that match nothing are sent as typed, and the page says nothing was found.
 */
function typedCode(): string {
  const typed = (draft.code ?? '').trim()
  if (!typed || looksLikeCode(typed)) return typed
  const plain = plainNameMatches(typed)[0]
  if (plain) return plain.code
  return codeItems.value[0]?.code ?? typed
}

function onSubmit() {
  const values = {
    code: typedCode(),
    npi: draft.npi ?? '',
    ein: draft.ein ?? '',
    provider: draft.provider ?? '',
    specialty: draft.specialty ?? '',
    county: draft.county ?? '',
  }
  localErrors.value = validateSearchForm(values)
  if (Object.keys(localErrors.value).length > 0) return

  const code = values.code.trim()
  emit('submit', {
    code,
    // A code type picked from a hint only applies to the same code.
    codeType: code.toUpperCase() === props.params.code.toUpperCase() ? props.params.codeType : null,
    npi: values.npi.trim() || null,
    ein: values.ein.trim() || null,
    provider: values.provider.trim() || null,
    specialty: values.specialty.trim() || null,
    county: values.county.trim() || null,
    product: draft.product,
    setting: draft.setting,
    feeType: draft.feeType,
    group: null,
  })
}
</script>

<template>
  <v-form validate-on="submit" data-testid="search-form" @submit.prevent="onSubmit">
    <div class="search-row">
      <v-combobox
        v-model="draft.code"
        v-model:search="codeSearch"
        :items="codeItems"
        item-title="code"
        item-value="code"
        :return-object="false"
        label="Service or code"
        placeholder="e.g. colonoscopy or 45378"
        :error-messages="errorFor('code')"
        :loading="codeSuggestions.isFetching.value"
        no-filter
        hide-no-data
        autocomplete="off"
        persistent-placeholder
        class="search-field"
        data-testid="code-input"
        @keydown.enter="onComboboxEnter"
      >
        <template #item="{ props: itemProps, item }">
          <v-list-item v-bind="itemProps" :title="serviceTitle(item.code, item.name)" :subtitle="describeCode(item)" />
        </template>
      </v-combobox>
      <v-btn type="submit" color="primary" height="40" :prepend-icon="mdiMagnify" :loading="busy" class="search-button" data-testid="search-submit">
        Search
      </v-btn>
    </div>

    <v-expansion-panels v-model="filtersOpen" multiple variant="accordion" flat class="filters mt-1">
      <v-expansion-panel>
        <v-expansion-panel-title class="text-body-medium" data-testid="filters-toggle">
          Filters<template v-if="activeFilterCount"> ({{ activeFilterCount }})</template>
        </v-expansion-panel-title>
        <v-expansion-panel-text>
          <v-row density="compact">
            <v-col cols="12" sm="6" md="4">
              <v-text-field
                v-model="draft.npi"
                label="NPI"
                inputmode="numeric"
                autocomplete="off"
                clearable
                :error-messages="errorFor('npi')"
                :messages="npiWarning && draft.npi !== (params.npi ?? '') ? [npiWarning] : []"
                data-testid="npi-input"
              >
                <template #message="{ message }">
                  <span :class="npiWarning === message ? 'text-warning' : undefined">{{ message }}</span>
                </template>
              </v-text-field>
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-text-field
                v-model="draft.ein"
                label="Tax ID (EIN)"
                placeholder="11-3471596"
                autocomplete="off"
                clearable
                :error-messages="errorFor('ein')"
                data-testid="ein-input"
              />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.product" :items="productItems" label="Plan" clearable placeholder="Both plans" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.setting" :items="settingItems" label="Place" clearable placeholder="Any place" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.feeType" :items="feeTypes" label="Bill" clearable placeholder="Both bills" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-combobox
                v-model="draft.provider"
                v-model:search="providerSearch"
                :items="providerItems"
                item-title="businessName"
                item-value="businessName"
                :return-object="false"
                label="Provider name"
                no-filter
                hide-no-data
                clearable
                autocomplete="off"
                :error-messages="errorFor('provider')"
                data-testid="provider-input"
                @keydown.enter="onComboboxEnter"
              >
                <template #item="{ props: itemProps, item }">
                  <v-list-item
                    v-bind="itemProps"
                    :subtitle="providerSuggestionLine(item)"
                    :lines="item.listedAs ? 'two' : 'one'"
                  />
                </template>
              </v-combobox>
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-combobox
                v-model="draft.specialty"
                v-model:search="specialtySearch"
                :items="specialtyItems"
                item-title="specialty"
                item-value="specialty"
                :return-object="false"
                label="Specialty"
                placeholder="e.g. gastro"
                persistent-placeholder
                no-filter
                hide-no-data
                clearable
                autocomplete="off"
                :error-messages="errorFor('specialty')"
                data-testid="specialty-input"
                @keydown.enter="onComboboxEnter"
              >
                <template #item="{ props: itemProps, item }">
                  <v-list-item v-bind="itemProps" :subtitle="item.group ?? undefined" />
                </template>
              </v-combobox>
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-autocomplete
                v-model="draft.county"
                v-model:search="countySearch"
                :items="locationItems"
                :filter-keys="['title', 'raw.county']"
                label="Location"
                placeholder="County or borough"
                persistent-placeholder
                clearable
                auto-select-first
                autocomplete="off"
                :error-messages="errorFor('county')"
                data-testid="county-input"
                @keydown.enter="onComboboxEnter"
                @blur="keepTypedLocation"
              >
                <template #item="{ props: itemProps, item }">
                  <v-list-item v-bind="itemProps" :subtitle="item.subtitle" />
                </template>
              </v-autocomplete>
            </v-col>
          </v-row>
          <v-btn variant="text" size="small" class="px-0" @click="emit('clear')">Start over</v-btn>
        </v-expansion-panel-text>
      </v-expansion-panel>
    </v-expansion-panels>
  </v-form>
</template>

<style scoped>
.search-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: flex-start;
}

.search-field {
  flex: 1 1 320px;
  min-width: 0;
}

.search-button {
  flex: 0 0 auto;
}

/* Filters read as a quiet text toggle under the search box, not a bar. */
.filters :deep(.v-expansion-panel) {
  background: transparent;
}

.filters :deep(.v-expansion-panel-title) {
  min-height: 40px;
  padding: 0;
  font-weight: 600;
  color: rgb(var(--v-theme-primary));
  justify-content: flex-start;
  gap: 4px;
}

.filters :deep(.v-expansion-panel-title__overlay) {
  display: none;
}

.filters :deep(.v-expansion-panel-title__icon) {
  margin-inline-start: 0;
}

.filters :deep(.v-expansion-panel-text__wrapper) {
  padding: 8px 0 0;
}
</style>
