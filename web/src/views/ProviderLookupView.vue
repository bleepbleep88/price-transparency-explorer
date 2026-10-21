<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiMagnify } from '@mdi/js'
import { plural } from '@/lib/format'
import { providerName } from '@/lib/plainNames'
import PageHeader from '@/components/simple/PageHeader.vue'
import type { ProviderSuggestion } from '@/features/rates/api/types'
import { useProviderSuggestions } from '@/features/rates/composables/useRateQueries'
import { parseLookupQuery } from '@/features/insights/lib/insightParams'

const route = useRoute()
const router = useRouter()

// The typed text lives in the address (replaced, not pushed, so typing doesn't fill the history).
const search = ref('')
watch(() => parseLookupQuery(route.query).q, (value) => (search.value = value ?? ''), { immediate: true })
const suggestions = useProviderSuggestions(search, undefined)
const items = computed(() => (suggestions.data.value ?? []).map((item) => ({ ...item, title: providerName(item.businessName) })))

// Picking a provider also sets the field's text to its name; that update must not replace the way to its scorecard.
let leaving = false

function onSearch(value: string | null) {
  search.value = value ?? ''
  const q = search.value.trim()
  if (!leaving && q !== (parseLookupQuery(route.query).q ?? '')) void router.replace({ name: 'providers', query: q ? { q } : {} })
}

function open(item: ProviderSuggestion | null) {
  if (!item) return
  leaving = true
  void router.push({ name: 'scorecard', params: { key: item.providerKey } })
}
</script>

<template>
  <v-container class="py-10 d-flex flex-column ga-6" max-width="960">
    <PageHeader title="Providers" subtitle="How one place's prices compare." />

    <v-autocomplete
      :search="search"
      :items="items"
      item-title="title"
      item-value="providerKey"
      return-object
      label="Provider"
      placeholder="Name, e.g. Northwell"
      persistent-placeholder
      :prepend-inner-icon="mdiMagnify"
      :loading="suggestions.isFetching.value"
      no-filter
      hide-no-data
      hide-details
      autocomplete="off"
      data-testid="provider-lookup"
      @update:search="onSearch"
      @update:model-value="open"
    >
      <template #item="{ props: itemProps, item }">
        <v-list-item v-bind="itemProps" :subtitle="plural(item.rates, 'price')" />
      </template>
    </v-autocomplete>
  </v-container>
</template>
