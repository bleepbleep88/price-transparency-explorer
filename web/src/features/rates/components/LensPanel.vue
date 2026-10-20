<script setup lang="ts">
import { computed } from 'vue'
import type { OutOfNetworkResult } from '@/features/out-of-network/api/types'
import type { RateSearchResult } from '../api/types'
import { LENS_OPTIONS, lensOption } from '../lib/lenses'
import type { Lens, QueryPatch, SearchParams } from '../lib/searchParams'
import EmployersLens from './lenses/EmployersLens.vue'
import InnovatorsLens from './lenses/InnovatorsLens.vue'
import LifeSciencesLens from './lenses/LifeSciencesLens.vue'
import PlansLens from './lenses/PlansLens.vue'
import ProvidersLens from './lenses/ProvidersLens.vue'

const props = defineProps<{
  result: RateSearchResult
  params: SearchParams
  /** The searched code's out-of-network payments, once loaded: the plans and providers lenses add one line. */
  outOfNetwork?: OutOfNetworkResult | null
}>()
const emit = defineEmits<{
  update: [patch: QueryPatch]
  cases: [cases: number | null]
  focusNpi: []
  search: [code: string]
}>()

const lens = computed(() => props.params.lens ?? null)
const answers = computed(() => (lens.value ? lensOption(lens.value).answers : null))

// A lens only changes the reading, so the reader stays on the same page of results. Picking the active one clears it.
function choose(next: unknown) {
  emit('update', { lens: (next as Lens | undefined) ?? null, page: props.params.page })
}
</script>

<template>
  <v-card data-testid="lens-panel">
    <div class="d-flex flex-wrap align-center gc-4 px-4 pt-3" :class="lens ? 'pb-1' : 'pb-3'">
      <h2 id="lens-heading" class="text-title-large">{{ lens ? 'What this means for you' : 'What does this mean for…' }}</h2>
      <v-chip-group
        :model-value="lens"
        selected-class="text-primary"
        column
        role="group"
        aria-label="Audience"
        @update:model-value="choose"
      >
        <v-chip
          v-for="option in LENS_OPTIONS"
          :key="option.key"
          :value="option.key"
          role="button"
          :aria-pressed="lens === option.key"
          variant="outlined"
          filter
          :data-testid="`lens-pick-${option.key}`"
        >
          {{ option.label }}
        </v-chip>
      </v-chip-group>
    </div>

    <v-card-text v-if="lens" class="pt-1 d-flex flex-column ga-3">
      <p class="text-body-medium text-medium-emphasis">{{ answers }}</p>
      <ProvidersLens
        v-if="lens === 'providers'"
        :result="result"
        :params="params"
        :out-of-network="outOfNetwork"
        @focus-npi="emit('focusNpi')"
      />
      <PlansLens
        v-else-if="lens === 'plans'"
        :result="result"
        :params="params"
        :out-of-network="outOfNetwork"
        @update="emit('update', $event)"
      />
      <EmployersLens v-else-if="lens === 'employers'" :result="result" :params="params" @cases="emit('cases', $event)" />
      <InnovatorsLens v-else-if="lens === 'innovators'" :result="result" :params="params" />
      <LifeSciencesLens v-else :result="result" @search="emit('search', $event)" />
    </v-card-text>
  </v-card>
</template>
