<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { mdiMagnify } from '@mdi/js'
import { plural } from '@/lib/format'
import type { ProviderSuggestion } from '@/features/rates/api/types'
import { useProviderSuggestions } from '@/features/rates/composables/useRateQueries'

const router = useRouter()
const search = ref('')
const suggestions = useProviderSuggestions(search, undefined)
const items = computed<ProviderSuggestion[]>(() => suggestions.data.value ?? [])

function open(item: ProviderSuggestion | null) {
  if (item) void router.push({ name: 'scorecard', params: { key: item.providerKey } })
}
</script>

<template>
  <v-container class="py-6" max-width="880">
    <h1 class="text-headline-medium mb-1">Provider scorecards</h1>
    <p class="text-body-medium text-medium-emphasis mb-4">
      One provider (tax ID) across every service it has a rate for: where each rate sits among other providers for the
      same service, and how its prices compare overall.
    </p>

    <v-autocomplete
      v-model:search="search"
      :items="items"
      item-title="businessName"
      item-value="providerKey"
      return-object
      label="Provider name or EIN"
      placeholder="e.g. Northwell, or 11-3471596"
      persistent-placeholder
      :prepend-inner-icon="mdiMagnify"
      :loading="suggestions.isFetching.value"
      no-filter
      hide-no-data
      autocomplete="off"
      data-testid="provider-lookup"
      @update:model-value="open"
    >
      <template #item="{ props: itemProps, item }">
        <v-list-item v-bind="itemProps" :subtitle="`EIN ${item.taxIdDisplay} · ${plural(item.rates, 'rate')}`" />
      </template>
    </v-autocomplete>

    <p class="text-body-small text-medium-emphasis mt-3">
      From a search, add an NPI or EIN, pick the Providers lens, and follow "See this provider's scorecard".
    </p>
  </v-container>
</template>
