<script setup lang="ts">
import { computed } from 'vue'
import { mdiCalendarMonthOutline, mdiMenuDown } from '@mdi/js'
import { shortDate } from '@/lib/format'
import { useSnapshotChoice } from '../composables/useSnapshotChoice'
import { snapshotLabel, snapshotNote } from '../lib/snapshots'

// Which monthly file the pages show. The newest is the default; every other one is one pick away.
const { snapshots, current, choose } = useSnapshotChoice()

const label = computed(() => snapshotLabel(current.value, snapshots.value))
</script>

<template>
  <v-menu v-if="snapshots.length > 1 && current" location="bottom end">
    <template #activator="{ props: menu }">
      <v-btn
        v-bind="menu"
        variant="text"
        :prepend-icon="mdiCalendarMonthOutline"
        :append-icon="mdiMenuDown"
        :aria-label="`Snapshot: the plan's ${shortDate(current.indexDate)} file. Change snapshot`"
        data-testid="snapshot-picker"
      >
        <span class="d-none d-sm-inline">{{ shortDate(current.indexDate) }}</span>
        <span class="d-sm-none">{{ label }}</span>
      </v-btn>
    </template>
    <v-list density="compact" aria-label="Snapshots" nav min-width="260">
      <v-list-subheader>The plan's monthly files</v-list-subheader>
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
