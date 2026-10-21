<script setup lang="ts">
import { mdiMenuDown } from '@mdi/js'
import { shortDate } from '@/lib/format'
import { useSnapshotChoice } from '../composables/useSnapshotChoice'
import { snapshotNote } from '../lib/snapshots'

// Which month's file the pages show: just its date (decision 90). The newest is the default; the others are one pick away.
const { snapshots, current, choose } = useSnapshotChoice()
</script>

<template>
  <v-menu v-if="snapshots.length > 1 && current" location="bottom end">
    <template #activator="{ props: menu }">
      <v-btn
        v-bind="menu"
        variant="text"
        :append-icon="mdiMenuDown"
        :aria-label="`Month: ${shortDate(current.indexDate)}. Pick another month`"
        data-testid="snapshot-picker"
      >
        {{ shortDate(current.indexDate) }}
      </v-btn>
    </template>
    <v-list density="compact" aria-label="Months" nav min-width="220">
      <v-list-subheader>Month</v-list-subheader>
      <v-list-item
        v-for="snapshot in snapshots"
        :key="snapshot.snapshotId"
        :title="shortDate(snapshot.indexDate)"
        :subtitle="snapshotNote(snapshot)"
        :active="snapshot.snapshotId === current.snapshotId"
        color="primary"
        :data-testid="`snapshot-option-${snapshot.indexDate}`"
        @click="choose(snapshot)"
      />
    </v-list>
  </v-menu>
</template>
