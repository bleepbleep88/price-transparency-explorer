<script setup lang="ts">
import { computed } from 'vue'
import { serviceTitle } from '@/lib/plainNames'
import type { LocationOption, ProductFileSummary, RateSearchResult, SettingOption } from '../api/types'
import { activeFilterChips } from '../lib/filterChips'
import type { QueryPatch, SearchParams } from '../lib/searchParams'

// The service's plain name with its code small, the filters in use (each removable) and any hint about the result.
const props = defineProps<{
  result: RateSearchResult
  params: SearchParams
  products: ProductFileSummary[]
  settings: SettingOption[]
  locations?: LocationOption[]
}>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const code = computed(() => props.result.code)
const title = computed(() => serviceTitle(code.value?.code ?? props.result.query.code, code.value?.name))
const chips = computed(() => activeFilterChips(props.result, props.params, props.products, props.settings, props.locations ?? []))
</script>

<template>
  <section class="service" aria-labelledby="result-heading">
    <div class="title-row">
      <h2 id="result-heading" class="service-title" data-testid="service-title">{{ title }}</h2>
      <span class="code" data-testid="service-code">{{ code ? `${code.codeType} ${code.code}` : result.query.code }}</span>
    </div>

    <div v-if="chips.length" class="chips" aria-label="Filters in use">
      <v-chip
        v-for="chip in chips"
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

    <v-alert
      v-for="(hint, index) in result.hints"
      :key="`${hint.kind}-${index}`"
      :type="hint.severity === 'warning' ? 'warning' : 'info'"
      variant="tonal"
      density="compact"
      :icon="false"
      :data-testid="`hint-${hint.kind}`"
    >
      <p>{{ hint.message }}</p>
      <div v-if="hint.links.length" class="d-flex flex-wrap ga-2 mt-2">
        <v-btn v-for="link in hint.links" :key="link.label" size="small" variant="outlined" @click="emit('update', { ...link.query, group: null })">
          {{ link.label }}
        </v-btn>
      </div>
    </v-alert>
  </section>
</template>

<style scoped>
.service {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.title-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 12px;
}

.service-title {
  font-family: var(--v-font-heading);
  font-size: 2rem;
  font-weight: 600;
  line-height: 1.15;
}

.code {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
</style>
