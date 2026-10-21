<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import type { RateGroup, RateSearchResult } from '../api/types'
import { ROLE_OPTIONS, roleLines } from '../lib/answer'
import { compareLink, gapsLink, scorecardLink } from '@/features/insights/lib/insightLinks'
import { apiSearchUrl } from '../lib/lenses'
import { MAX_CASES, npiCheckDigitWarning, PROVIDER_ID_ERROR, providerIdPatch, type Lens, type QueryPatch, type SearchParams } from '../lib/searchParams'

// "I'm a…": one or two short lines for the reader's role, about the bill picked above.
const props = defineProps<{ result: RateSearchResult; params: SearchParams; group: RateGroup | null; tiles?: RateGroup[] }>()
const emit = defineEmits<{
  update: [patch: QueryPatch]
  cases: [cases: number | null]
  search: [code: string]
}>()

const lens = computed(() => props.params.lens ?? null)
const lines = computed(() => (lens.value ? roleLines(lens.value, props.result, props.group, props.params.cases, props.tiles ?? []) : []))
const showCases = computed(() => lens.value === 'employers' && lines.value.some((line) => line.cases))
const apiUrl = computed(() => apiSearchUrl(props.params, window.location.origin))
// One link on to the page that answers the role's next question, when that page has something for this service.
const nextLink = computed(() => {
  if (lens.value === 'providers') {
    const scorecard = scorecardLink(props.result)
    if (!scorecard) return null
    // The scorecard is titled with the tax ID's main name; it says it's the same provider as the one searched.
    const to = typeof scorecard.to === 'object' && 'query' in scorecard.to ? { ...scorecard.to, query: { ...scorecard.to.query, as: scorecard.name } } : scorecard.to
    return { to, label: 'All your prices' }
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

// The provider role asks for an NPI or tax ID right here, under its line, and "Change" brings the box back.
const searchedId = computed(() => props.params.npi ?? props.params.ein ?? null)
const askForId = computed(() => lens.value === 'providers' && !searchedId.value && lines.value.some((line) => line.action === 'npi'))
const idText = ref('')
const idError = ref('')
const idWarning = computed(() => npiCheckDigitWarning(idText.value))
const idField = useTemplateRef<{ focus: () => void }>('idField')
let focusBox = false

function onIdInput(value: string | null) {
  idText.value = value ?? ''
  idError.value = ''
}

function showMine() {
  const patch = providerIdPatch(idText.value)
  if (!patch) {
    idError.value = PROVIDER_ID_ERROR
    return
  }
  emit('update', { ...patch, lens: 'providers' })
}

function changeId() {
  idText.value = searchedId.value ?? ''
  idError.value = ''
  focusBox = true
  emit('update', { npi: null, ein: null })
}

// After "Change", the box comes back once the search without the ID is on screen: put the cursor in it.
watch(askForId, async (asking) => {
  if (!asking || !focusBox) return
  focusBox = false
  await nextTick()
  idField.value?.focus()
})

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
        <div class="role-row">
          <p class="role-line" data-testid="role-line">{{ line.text }}</p>
          <v-btn
            v-if="lens === 'providers' && searchedId && index === lines.length - 1"
            variant="text"
            size="small"
            color="primary"
            class="px-1"
            aria-label="Change NPI or tax ID"
            data-testid="change-id"
            @click="changeId"
          >
            Change
          </v-btn>
        </div>
        <div v-if="line.action === 'api'">
          <v-btn variant="text" size="small" class="px-0" color="primary" :href="apiUrl" target="_blank" rel="noopener" :append-icon="mdiOpenInNew">
            This search in the API
          </v-btn>
        </div>
        <div v-else-if="line.action === 'drug'">
          <v-btn variant="outlined" size="small" @click="emit('search', 'J0585')">See Botox</v-btn>
        </div>
      </template>
      <form v-if="askForId" class="your-id" data-testid="your-id" @submit.prevent="showMine">
        <v-text-field
          ref="idField"
          :model-value="idText"
          label="Your NPI or tax ID"
          inputmode="numeric"
          autocomplete="off"
          density="compact"
          :error-messages="idError"
          :messages="idWarning ? [idWarning] : []"
          hide-details="auto"
          class="your-id-field"
          data-testid="your-id-input"
          @update:model-value="onIdInput"
        />
        <v-btn type="submit" color="primary" variant="flat" data-testid="your-id-show">Show me</v-btn>
      </form>
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
  gap: 16px;
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

.role-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  column-gap: 8px;
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

.your-id {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 8px 16px;
}

.your-id-field {
  flex: 0 1 260px;
  min-width: 200px;
}

.your-id .v-btn {
  height: 40px;
}
</style>
