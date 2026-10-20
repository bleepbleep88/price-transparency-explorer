<script setup lang="ts">
import { computed } from 'vue'
import { mdiOpenInNew, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { shortDate } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useDataStatus } from '@/features/status/useDataStatus'
import SourceFileCard from '@/features/sources/components/SourceFileCard.vue'
import { useSources } from '@/features/sources/composables/useSources'
import { CLEANING_RULES, STATUS_COLORS } from '@/features/sources/lib/sourceText'

const sources = useSources()
const { status } = useDataStatus()

const errorMessage = computed(() =>
  sources.error.value instanceof ApiError ? sources.error.value.message : sources.error.value ? 'Could not load the sources.' : null,
)
const stillLoading = computed(() => status.value != null && status.value.phase !== 'ready' && status.value.phase !== 'failed')
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Sources and data quality</h1>
    <p class="text-body-medium text-medium-emphasis mb-4">
      Every file in the plan's list of files: where it came from, what was loaded, how it was cleaned and what was set aside.
      Every rate on the search page traces back to one of these files.
    </p>

    <v-card class="mb-6" data-testid="cleaning-rules">
      <v-card-item>
        <v-card-title tag="h2">How every file is cleaned</v-card-title>
      </v-card-item>
      <v-card-text>
        <ul class="text-body-medium ps-4">
          <li v-for="rule in CLEANING_RULES" :key="rule">{{ rule }}</li>
        </ul>
      </v-card-text>
    </v-card>

    <v-skeleton-loader v-if="sources.isPending.value" type="heading, card@2" />
    <v-alert v-else-if="errorMessage" type="error" variant="tonal">
      {{ errorMessage }}
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="sources.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <template v-else-if="sources.data.value">
      <LoadingPanel v-if="stillLoading" class="mb-6" />
      <v-empty-state
        v-if="!sources.data.value.length && !stillLoading"
        headline="Nothing loaded yet"
        text="The plan's list of files couldn't be read. The data status in the top bar says why."
      />

      <section
        v-for="snapshot in sources.data.value"
        :key="snapshot.snapshotId"
        class="mb-8 d-flex flex-column ga-2"
        :aria-labelledby="`snapshot-${snapshot.snapshotId}`"
        data-testid="source-snapshot"
      >
        <div class="d-flex flex-wrap align-center ga-2">
          <h2 :id="`snapshot-${snapshot.snapshotId}`" class="text-title-large">Snapshot {{ shortDate(snapshot.indexDate) }}</h2>
          <v-chip :color="STATUS_COLORS[snapshot.status] ?? 'default'" variant="tonal">{{ snapshot.status }}</v-chip>
          <v-chip v-if="snapshot.isLatest" variant="outlined">Shown by default</v-chip>
        </div>
        <p class="text-body-medium text-medium-emphasis">
          List of files from {{ snapshot.reportingEntityName ?? 'the plan' }}<template v-if="snapshot.reportingEntityType">
            ({{ snapshot.reportingEntityType }})</template>, schema {{ snapshot.schemaVersion ?? 'not stated' }}. The list
          says it was last updated {{ shortDate(snapshot.indexLastUpdatedOn) }}.
        </p>
        <p v-if="snapshot.error" class="text-body-small text-warning">{{ snapshot.error }}</p>
        <div class="d-flex flex-wrap align-center ga-2">
          <v-btn :href="snapshot.indexUrl" target="_blank" rel="noopener" variant="text" size="small" class="px-0" :append-icon="mdiOpenInNew">
            List of files
          </v-btn>
          <code v-if="snapshot.indexSha256" class="text-body-small text-break">SHA-256 {{ snapshot.indexSha256 }}</code>
        </div>

        <v-row class="mt-1">
          <v-col v-for="file in snapshot.files" :key="file.fileId" cols="12" md="6">
            <SourceFileCard :file="file" />
          </v-col>
        </v-row>
      </section>
    </template>
  </v-container>
</template>
