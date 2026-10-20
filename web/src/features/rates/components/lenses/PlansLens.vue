<script setup lang="ts">
import { computed } from 'vue'
import { money, plural } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { spreadLines, spreadText, widestSpread } from '../../lib/lenses'
import type { QueryPatch, SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const lines = computed(() => spreadLines(props.result))
const widest = computed(() => widestSpread(lines.value))
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-plans">
    <p v-if="!lines.length" class="text-body-medium">No group here has enough providers to measure spread.</p>
    <template v-else>
      <p class="text-body-medium">
        Widest spread first. The gap to the lower quartile is room to steer care toward lower-priced providers.
      </p>
      <div v-for="line in widest" :key="line.groupKey" data-testid="spread-line">
        <p class="text-body-medium font-weight-medium">{{ line.group }}</p>
        <p class="text-body-small text-medium-emphasis">{{ plural(line.providers, 'provider') }}, median {{ money(line.median) }}</p>
        <p class="text-body-medium">{{ spreadText(line) }}</p>
      </div>
      <p v-if="lines.length > widest.length" class="text-body-small text-medium-emphasis">
        {{ plural(lines.length - widest.length, 'more group') }} with statistics in the cards below.
      </p>
      <v-btn
        class="align-self-start"
        variant="tonal"
        color="primary"
        :disabled="params.sort === '-rate'"
        data-testid="sort-highest"
        @click="emit('update', { sort: '-rate', page: 1 })"
      >
        {{ params.sort === '-rate' ? 'Sorted by highest rate' : 'Sort providers by highest rate' }}
      </v-btn>
    </template>
  </div>
</template>
