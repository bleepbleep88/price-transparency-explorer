<script setup lang="ts">
import { computed } from 'vue'
import { plural } from '@/lib/format'
import { typicalLine } from '@/lib/plainNumbers'
import type { OutOfNetworkResult } from '@/features/out-of-network/api/types'
import { plansLensLine, visibleGroups } from '@/features/out-of-network/lib/outOfNetworkText'
import { compareLink, gapsLink, hasRankedGroup } from '@/features/insights/lib/insightLinks'
import type { RateSearchResult } from '../../api/types'
import { hasProviderFilter, spreadLines, spreadText, widestSpread } from '../../lib/lenses'
import type { QueryPatch, SearchParams } from '../../lib/searchParams'

const props = defineProps<{ result: RateSearchResult; params: SearchParams; outOfNetwork?: OutOfNetworkResult | null }>()
const emit = defineEmits<{ update: [patch: QueryPatch] }>()

const lines = computed(() => spreadLines(props.result))
const varying = computed(() => widestSpread(lines.value, lines.value.length))
const widest = computed(() => varying.value.slice(0, 3))
const outOfNetworkLine = computed(() => plansLensLine(visibleGroups(props.outOfNetwork, props.params)))
const ranked = computed(() => hasRankedGroup(props.result))
const compare = computed(() => compareLink(props.result))

// The spread line describes everyone in the group, so its highest rates are shown for everyone, not only the filtered providers.
function showHighest(groupKey: string) {
  const everyone = hasProviderFilter(props.result) ? { npi: null, ein: null, provider: null, specialty: null, county: null } : {}
  emit('update', { group: groupKey, sort: '-rate', page: 1, ...everyone })
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
    <div class="d-flex flex-wrap ga-2">
      <v-btn :to="gapsLink(result, 'spread')" variant="text" color="primary" class="px-1" data-testid="lens-gaps-link">
        {{ ranked ? 'See how widely prices range for this code' : 'See where prices range widest' }}
      </v-btn>
      <v-btn v-if="compare" :to="compare" variant="text" color="primary" class="px-1" data-testid="lens-compare-link">
        Compare the two plans for this code
      </v-btn>
    </div>
  </div>
</template>
