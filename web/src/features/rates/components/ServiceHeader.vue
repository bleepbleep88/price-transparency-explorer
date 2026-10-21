<script setup lang="ts">
import { computed } from 'vue'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import type { LocationOption, ProductFileSummary, RateSearchResult, SettingOption } from '../api/types'
import { activeFilterChips } from '../lib/filterChips'
import type { QueryPatch, SearchParams } from '../lib/searchParams'
import { resultTitle, TECHNICAL_HINTS } from '../lib/answer'

// The page's heading: the service's plain name with its code small, the filters in use (each removable) and any hint
// about the result. With no prices, one short line, the hint's links, and its full text under "Why".
const props = defineProps<{
  result: RateSearchResult
  params: SearchParams
  products: ProductFileSummary[]
  settings: SettingOption[]
  locations?: LocationOption[]
}>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const code = computed(() => props.result.code?.code ?? props.result.query.code)
const title = computed(() => resultTitle(props.result))
// "Code 99999" already says the code.
const showCode = computed(() => !title.value.includes(code.value))
const chips = computed(() => activeFilterChips(props.result, props.params, props.products, props.settings, props.locations ?? []))
const empty = computed(() => !props.result.groups.length)
// How the plan filed the code (revenue codes, procedure lists) is for Details once there are prices to show.
const hints = computed(() => props.result.hints.filter((hint) => !TECHNICAL_HINTS.has(hint.kind)))
const links = computed(() => props.result.hints.flatMap((hint) => hint.links))
</script>

<template>
  <section class="service" aria-labelledby="result-heading">
    <div class="title-row">
      <h1 id="result-heading" class="service-title" data-testid="service-title">{{ title }}</h1>
      <span v-if="showCode" class="code" data-testid="service-code">{{ code }}</span>
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

    <!-- No prices: one short line and what to try; the reasons are under "Why". -->
    <template v-if="empty">
      <p class="empty-line" data-testid="no-prices">No prices for this in these files.</p>
      <div v-if="links.length" class="d-flex flex-wrap ga-2">
        <v-btn v-for="link in links" :key="link.label" size="small" variant="outlined" @click="emit('update', { ...link.query, group: null })">
          {{ link.label }}
        </v-btn>
      </div>
      <DetailsPanel label="Why" testid="no-prices-why">
        <p v-for="(hint, index) in result.hints" :key="index" :data-testid="`hint-${hint.kind}`">{{ hint.message }}</p>
      </DetailsPanel>
    </template>

    <template v-else>
      <v-alert
        v-for="(hint, index) in hints"
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
    </template>
  </section>
</template>

<style scoped>
.service {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.title-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 16px;
}

.service-title {
  font-family: var(--v-font-heading);
  font-size: 2rem;
  font-weight: 600;
  line-height: 1.15;
  text-wrap: balance;
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

.empty-line {
  font-size: 1rem;
}
</style>
