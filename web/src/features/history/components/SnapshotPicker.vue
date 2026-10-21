<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiMenuDown } from '@mdi/js'
import { shortDate } from '@/lib/format'
import { useSnapshotChoice } from '../composables/useSnapshotChoice'
import { snapshotNote } from '../lib/snapshots'

// Which month's file the pages show: just its date (decision 90). The newest is the default; the others are one pick away.
const { snapshots, current, choose } = useSnapshotChoice()
// On a phone the year goes ("Sep 28"), so the app bar fits beside the brand.
const { xs } = useDisplay()
const label = computed(() => {
  const date = shortDate(current.value?.indexDate ?? null)
  return xs.value ? date.replace(/,\s*\d{4}$/, '') : date
})
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
        {{ label }}
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
