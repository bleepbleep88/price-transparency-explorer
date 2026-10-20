<script setup lang="ts">
import { money } from '@/lib/format'
import type { ScorecardGroup } from '../api/types'
import { searchLocation } from '../lib/insightLinks'
import { serviceName } from '../lib/insightText'
import { groupLine, position, unitSuffix } from '../lib/plainWords'

// A provider's services furthest above (or below) the typical price, each opening the search that shows it. The list is
// ranked by share of the typical price, so each line says that share next to the dollars.
defineProps<{ title: string; groups: ScorecardGroup[]; empty: string }>()

function detail(group: ScorecardGroup): string {
  const unit = unitSuffix(group)
  const { difference } = position(group.band, group.value, group.median, unit)
  return difference ? `${money(group.value)}${unit}: ${difference}` : `${money(group.value)}${unit}`
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
          <!-- Plain text, not a list subtitle: subtitles are cut to one line, and the setting and plan need all of it. -->
          <p class="text-body-small text-medium-emphasis">{{ groupLine(group) }} · {{ group.unit }}</p>
          <p class="text-body-medium">{{ detail(group) }}</p>
        </v-list-item>
      </v-list>
    </v-card-text>
  </v-card>
</template>
