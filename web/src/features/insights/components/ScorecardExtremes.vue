<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { lowerFirst } from '@/lib/plainNumbers'
import PlaceList, { type PlaceItem } from '@/components/simple/PlaceList.vue'
import type { ScorecardGroup } from '../api/types'
import { searchLocation } from '../lib/insightLinks'
import { rowLine, serviceLabel, versusTypical } from '../lib/plainWords'

// A provider's services furthest above (or below) the typical price: plain name, the bill and how far off, its price.
// Each opens the search that shows it.
const props = defineProps<{ title: string; groups: ScorecardGroup[]; tone: 'low' | 'high'; empty: string }>()
const router = useRouter()

const items = computed<PlaceItem[]>(() =>
  props.groups.map((group) => ({
    key: group.groupKey,
    name: serviceLabel(group),
    place: [rowLine(group), lowerFirst(versusTypical(group.value, group.median, group.band) ?? '')].filter(Boolean).join(' · '),
    price: group.value,
  })),
)

function open(key: string) {
  const group = props.groups.find((g) => g.groupKey === key)
  if (group) void router.push(searchLocation(group.search))
}
</script>

<template>
  <PlaceList v-if="items.length" :title="title" :items="items" :tone="tone" data-testid="scorecard-extremes" @open="open" />
  <section v-else class="d-flex flex-column ga-2" data-testid="scorecard-extremes">
    <h3 class="empty-title">{{ title }}</h3>
    <p class="text-body-medium text-medium-emphasis">{{ empty }}</p>
  </section>
</template>

<style scoped>
.empty-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}
</style>
