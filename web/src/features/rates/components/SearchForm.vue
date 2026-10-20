<script setup lang="ts">
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import { mdiMagnify } from '@mdi/js'
import { plural } from '@/lib/format'
import type {
  CodeSuggestion,
  LocationOption,
  ProductFileSummary,
  ProviderSuggestion,
  SettingOption,
  SpecialtySuggestion,
} from '../api/types'
import { useCodeSuggestions, useProviderSuggestions, useSpecialtySuggestions } from '../composables/useRateQueries'
import { locationSelectItems } from '../lib/locations'
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

const npiField = useTemplateRef<{ focus: () => void }>('npiField')
/** The providers lens sends readers here to add their NPI. */
function focusNpi() {
  npiField.value?.focus()
}
defineExpose({ focusNpi })

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
  const anyFilter = params.product || params.setting || params.feeType || params.provider || params.specialty || params.county
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
const codeItems = computed<CodeSuggestion[]>(() => codeSuggestions.data.value ?? [])
const providerItems = computed<ProviderSuggestion[]>(() => providerSuggestions.data.value ?? [])
const specialtySuggestions = useSpecialtySuggestions(specialtySearch)
const specialtyItems = computed<SpecialtySuggestion[]>(() => specialtySuggestions.data.value ?? [])
const locationItems = computed(() => locationSelectItems(props.locations ?? []))

const npiWarning = computed(() => npiCheckDigitWarning(draft.npi))
const feeTypes = [
  { title: 'Facility fee', value: 'facility' },
  { title: "Doctor's fee", value: 'professional' },
]
const productItems = computed(() => props.products.map((product) => ({ title: product.productLabel, value: product.productKey })))
const settingItems = computed(() => props.settings.map((setting) => ({ title: setting.label, value: setting.key })))

function errorFor(field: FormField): string[] {
  const local = localErrors.value[field]
  if (local) return [local]
  const external = props.externalErrors?.[field]
  if (!external) return []
  return Array.isArray(external) ? external : [external]
}

function describeCode(item: CodeSuggestion): string {
  const parts = [item.name ?? 'No description published by the plan', item.category]
  if (item.facilityRevenueCodes.length) {
    parts.push(`${item.billedDirectly ? 'also ' : 'only '}as a facility rate via revenue code ${item.facilityRevenueCodes.join(', ')}`)
  }
  parts.push(plural(item.rates, 'rate'))
  return parts.join(' · ')
}

// Vuetify's combobox keeps Enter for itself (to pick a suggestion), so the form never submits. Search once it's done.
function onComboboxEnter(event: KeyboardEvent) {
  if (event.defaultPrevented && !event.isComposing) void nextTick(onSubmit)
}

function onSubmit() {
  const values = {
    code: draft.code ?? '',
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
    <v-row density="compact">
      <v-col cols="12" md="5">
        <v-combobox
          v-model="draft.code"
          v-model:search="codeSearch"
          :items="codeItems"
          item-title="code"
          item-value="code"
          :return-object="false"
          label="Billing code"
          placeholder="e.g. 45378, 0490, J0585 or 'colonoscopy'"
          :error-messages="errorFor('code')"
          :loading="codeSuggestions.isFetching.value"
          no-filter
          hide-no-data
          autocomplete="off"
          persistent-placeholder
          data-testid="code-input"
          @keydown.enter="onComboboxEnter"
        >
          <template #item="{ props: itemProps, item }">
            <v-list-item v-bind="itemProps" :title="`${item.codeType} ${item.code}`" :subtitle="describeCode(item)" lines="two" />
          </template>
        </v-combobox>
      </v-col>
      <v-col cols="12" sm="6" md="3">
        <v-text-field
          ref="npiField"
          v-model="draft.npi"
          label="NPI (optional)"
          inputmode="numeric"
          autocomplete="off"
          clearable
          :error-messages="errorFor('npi')"
          :messages="npiWarning ? [npiWarning] : []"
          data-testid="npi-input"
        >
          <template #message="{ message }">
            <span :class="npiWarning === message ? 'text-warning' : undefined">{{ message }}</span>
          </template>
        </v-text-field>
      </v-col>
      <v-col cols="12" sm="6" md="2">
        <v-text-field
          v-model="draft.ein"
          label="EIN (optional)"
          placeholder="11-3471596"
          autocomplete="off"
          clearable
          :error-messages="errorFor('ein')"
          data-testid="ein-input"
        />
      </v-col>
      <v-col cols="12" md="2" class="d-flex align-start ga-2">
        <v-btn type="submit" color="primary" height="40" :prepend-icon="mdiMagnify" :loading="busy" block data-testid="search-submit">
          Search
        </v-btn>
      </v-col>
    </v-row>

    <v-expansion-panels v-model="filtersOpen" multiple variant="accordion" flat class="mt-1">
      <v-expansion-panel>
        <v-expansion-panel-title class="text-body-medium">
          More filters (product, setting, fee type, provider name, specialty, location)
        </v-expansion-panel-title>
        <v-expansion-panel-text>
          <v-row density="compact">
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.product" :items="productItems" label="Product" clearable placeholder="All products" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.setting" :items="settingItems" label="Setting" clearable placeholder="All settings" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-select v-model="draft.feeType" :items="feeTypes" label="Fee type" clearable placeholder="Facility and doctor's fees" persistent-placeholder />
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-combobox
                v-model="draft.provider"
                v-model:search="providerSearch"
                :items="providerItems"
                item-title="businessName"
                item-value="businessName"
                :return-object="false"
                label="Provider name contains"
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
                    :subtitle="`EIN ${item.taxIdDisplay} · ${plural(item.rates, 'rate')}`"
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
                placeholder="Any specialty, e.g. gastro"
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
                  <v-list-item v-bind="itemProps" :subtitle="`${item.group ?? 'Specialty'} · ${plural(item.npis, 'NPI')}`" />
                </template>
              </v-combobox>
            </v-col>
            <v-col cols="12" sm="6" md="4">
              <v-autocomplete
                v-model="draft.county"
                :items="locationItems"
                label="Location"
                placeholder="Any New York county or NYC borough"
                persistent-placeholder
                clearable
                auto-select-first
                :error-messages="errorFor('county')"
                data-testid="county-input"
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
