<script setup lang="ts">
import { money, ordinal, times } from '@/lib/format'
import { groupName } from '@/features/rates/lib/lenses'
import type { ScorecardGroup } from '../api/types'
import { searchLocation } from '../lib/insightLinks'
import { serviceName } from '../lib/insightText'

// A provider's groups furthest above (or below) the median, each opening the search that shows it.
defineProps<{ title: string; groups: ScorecardGroup[]; empty: string }>()

function detail(group: ScorecardGroup): string {
  const percentile = group.percentile == null ? '' : ` · ${ordinal(group.percentile)} percentile`
  return `${money(group.value)}, ${times(group.vsMedian)} the median${percentile}`
}
</script>

<template>
  <v-card class="h-100">
    <v-card-item>
      <v-card-title tag="h2">{{ title }}</v-card-title>
    </v-card-item>
    <v-card-text>
      <p v-if="!groups.length" class="text-body-medium text-medium-emphasis">{{ empty }}</p>
      <v-list v-else density="compact" class="py-0 bg-transparent">
        <v-list-item
          v-for="group in groups"
          :key="group.groupKey"
          :to="searchLocation(group.search)"
          class="px-0"
          data-testid="extreme-group"
        >
          <v-list-item-title class="text-body-medium font-weight-medium text-wrap">{{ serviceName(group) }}</v-list-item-title>
          <!-- Plain text, not a list subtitle: subtitles are cut to one line, and the group needs all of it. -->
          <p class="text-body-small text-medium-emphasis">{{ groupName(group) }}</p>
          <p class="text-body-medium">{{ detail(group) }}</p>
        </v-list-item>
      </v-list>
    </v-card-text>
  </v-card>
</template>
