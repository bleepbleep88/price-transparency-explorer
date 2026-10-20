<script setup lang="ts">
import { computed } from 'vue'
import { plural } from '@/lib/format'
import { typicalLine } from '@/lib/plainNumbers'
import type { OutOfNetworkResult } from '@/features/out-of-network/api/types'
import { plansLensLine, visibleGroups } from '@/features/out-of-network/lib/outOfNetworkText'
import type { RateSearchResult } from '../../api/types'
import { spreadLines, spreadText, widestSpread } from '../../lib/lenses'
import type { QueryPatch, SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams; outOfNetwork?: OutOfNetworkResult | null }>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const lines = computed(() => spreadLines(props.result))
const varying = computed(() => widestSpread(lines.value, lines.value.length))
const widest = computed(() => varying.value.slice(0, 3))
const outOfNetworkLine = computed(() => plansLensLine(visibleGroups(props.outOfNetwork, props.params)))

function showHighest(groupKey: string) {
  emit('update', { group: groupKey, sort: '-rate', page: 1 })
}
</script>

<template>
  <div class="d-flex flex-column ga-3" data-testid="lens-plans">
    <p v-if="!lines.length" class="text-body-medium">
      It takes 10 providers to say how much prices vary, and no card here has that many.
    </p>
    <p v-else-if="!widest.length" class="text-body-medium">
      Prices barely vary here: on each card with 10 or more providers, most providers get about the same price.
    </p>
    <template v-else>
      <p class="text-body-medium">Where prices vary most, first.</p>
      <div v-for="(line, index) in widest" :key="line.groupKey" class="d-flex flex-column ga-1" data-testid="spread-line">
        <p class="text-body-medium font-weight-medium">{{ line.group }}</p>
        <p class="text-body-small text-medium-emphasis">
          {{ plural(line.providers, 'provider') }} · {{ typicalLine(line.summary, line.perUnit ? 'per unit' : undefined) }}
        </p>
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
          See the highest prices for this one
        </v-btn>
      </div>
      <p v-if="varying.length > widest.length" class="text-body-small text-medium-emphasis">
        {{ plural(varying.length - widest.length, 'more card') }} where prices vary, below.
      </p>
    </template>
    <p v-if="outOfNetworkLine" class="text-body-medium" data-testid="lens-oon-line">{{ outOfNetworkLine }}</p>
  </div>
</template>
