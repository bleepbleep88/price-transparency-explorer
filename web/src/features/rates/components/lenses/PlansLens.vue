<script setup lang="ts">
import { computed } from 'vue'
import { money, plural } from '@/lib/format'
import type { RateSearchResult } from '../../api/types'
import { spreadLines, spreadText, widestSpread } from '../../lib/lenses'
import type { QueryPatch, SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams }>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const lines = computed(() => spreadLines(props.result))
const spread = computed(() => widestSpread(lines.value, lines.value.length))
const widest = computed(() => spread.value.slice(0, 3))

function showHighest(groupKey: string) {
  emit('update', { group: groupKey, sort: '-rate', page: 1 })
}
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-plans">
    <p v-if="!lines.length" class="text-body-medium">No group here has enough providers to measure spread.</p>
    <p v-else-if="!widest.length" class="text-body-medium">
      Rates don't spread here: in every group, the 90th percentile equals the 10th.
    </p>
    <template v-else>
      <p class="text-body-medium">Widest spread first. The gap to the lower quartile is room to steer care.</p>
      <div v-for="(line, index) in widest" :key="line.groupKey" class="d-flex flex-column ga-1" data-testid="spread-line">
        <p class="text-body-medium font-weight-medium">{{ line.group }}</p>
        <p class="text-body-small text-medium-emphasis">{{ plural(line.providers, 'provider') }}, median {{ money(line.median) }}</p>
        <p class="text-body-medium">{{ spreadText(line) }}</p>
        <v-btn
          v-if="index === 0"
          class="align-self-start mt-1"
          variant="tonal"
          color="primary"
          :disabled="params.group === line.groupKey && params.sort === '-rate'"
          data-testid="show-highest"
          @click="showHighest(line.groupKey)"
        >
          See the highest rates in this group
        </v-btn>
      </div>
      <p v-if="spread.length > widest.length" class="text-body-small text-medium-emphasis">
        {{ plural(spread.length - widest.length, 'more group') }} with a spread in the cards below.
      </p>
    </template>
  </div>
</template>
