<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import type { RateGroup, RateSearchResult } from '../api/types'
import { ROLE_OPTIONS, roleLines } from '../lib/answer'
import { compareLink, gapsLink, scorecardLink } from '@/features/insights/lib/insightLinks'
import { apiSearchUrl } from '../lib/lenses'
import { MAX_CASES, type Lens, type QueryPatch, type SearchParams } from '../lib/searchParams'

// "I'm a…": one or two short lines for the reader's role, about the bill picked above.
const props = defineProps<{ result: RateSearchResult; params: SearchParams; group: RateGroup | null }>()
const emit = defineEmits<{
  update: [patch: QueryPatch]
  cases: [cases: number | null]
  focusNpi: []
  search: [code: string]
}>()

const lens = computed(() => props.params.lens ?? null)
const lines = computed(() => (lens.value ? roleLines(lens.value, props.result, props.group, props.params.cases) : []))
const showCases = computed(() => lens.value === 'employers' && props.group && !props.group.perUnit)
const apiUrl = computed(() => apiSearchUrl(props.params, window.location.origin))
// One link on to the page that answers the role's next question, when that page has something for this service.
const nextLink = computed(() => {
  if (lens.value === 'providers') {
    const scorecard = scorecardLink(props.result)
    return scorecard ? { to: scorecard.to, label: 'All your prices' } : null
  }
  if (lens.value === 'plans') {
    const compare = compareLink(props.result)
    return compare ? { to: compare, label: 'Compare the two plans' } : null
  }
  if (lens.value === 'employers') return { to: gapsLink(props.result, 'gap'), label: 'Where prices differ most' }
  return null
})

const casesText = ref(props.params.cases ? String(props.params.cases) : '')
watch(
  () => props.params.cases,
  (cases) => (casesText.value = cases ? String(cases) : ''),
)

function choose(next: Lens) {
  emit('update', { lens: lens.value === next ? null : next, page: props.params.page })
}

function onCases(value: string | null) {
  casesText.value = value ?? ''
  const cases = Number((value ?? '').replace(/,/g, ''))
  emit('cases', Number.isInteger(cases) && cases >= 1 && cases <= MAX_CASES ? cases : null)
}
</script>

<template>
  <section class="roles" aria-label="For you" data-testid="lens-panel">
    <div class="role-chips" role="group" aria-label="I'm a">
      <span class="roles-label">I'm a</span>
      <v-chip
        v-for="option in ROLE_OPTIONS"
        :key="option.key"
        :variant="lens === option.key ? 'tonal' : 'outlined'"
        :color="lens === option.key ? 'primary' : undefined"
        :aria-pressed="lens === option.key"
        role="button"
        rounded="pill"
        :data-testid="`lens-pick-${option.key}`"
        @click="choose(option.key)"
      >
        {{ option.label }}
      </v-chip>
    </div>

    <div v-if="lens" class="role-lines" :data-testid="`lens-${lens}`">
      <template v-for="(line, index) in lines" :key="index">
        <p class="role-line" data-testid="role-line">{{ line.text }}</p>
        <div v-if="line.action === 'npi'">
          <v-btn variant="outlined" size="small" data-testid="add-npi" @click="emit('focusNpi')">Add NPI</v-btn>
        </div>
        <div v-else-if="line.action === 'api'">
          <v-btn variant="text" size="small" class="px-0" color="primary" :href="apiUrl" target="_blank" rel="noopener" :append-icon="mdiOpenInNew">
            This search in the API
          </v-btn>
        </div>
        <div v-else-if="line.action === 'drug'">
          <v-btn variant="outlined" size="small" @click="emit('search', 'J0585')">See Botox</v-btn>
        </div>
      </template>
      <div v-if="nextLink">
        <v-btn variant="text" size="small" class="px-0" color="primary" :to="nextLink.to" data-testid="role-link">{{ nextLink.label }}</v-btn>
      </div>
      <div v-if="showCases" class="cases">
        <v-text-field
          :model-value="casesText"
          label="Your cases a year"
          inputmode="numeric"
          density="compact"
          hide-details
          clearable
          class="cases-field"
          data-testid="cases-input"
          @update:model-value="onCases"
        />
        <p v-if="lines[0]?.yearly" class="role-line font-weight-medium" data-testid="yearly-gap">{{ lines[0].yearly }}</p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.roles {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 16px;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.role-chips {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.roles-label {
  font-weight: 600;
  margin-right: 4px;
}

.role-lines {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.role-line {
  font-size: 1rem;
}

.cases {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
}

.cases-field {
  flex: 0 0 200px;
}
</style>
