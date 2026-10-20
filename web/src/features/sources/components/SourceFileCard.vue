<script setup lang="ts">
import { computed } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import { shortDate, wholeNumber } from '@/lib/format'
import type { SourceFile } from '../api/types'
import { STATUS_COLORS, cleaningLines, contentsLine, fileSize, kindLabel, publishedLine, timingLine } from '../lib/sourceText'

const props = defineProps<{ file: SourceFile }>()

const published = computed(() => publishedLine(props.file))
const contents = computed(() => contentsLine(props.file))
const cleaning = computed(() => cleaningLines(props.file))
const timing = computed(() => timingLine(props.file))
</script>

<template>
  <v-card class="h-100 d-flex flex-column" data-testid="source-file">
    <v-card-item>
      <v-card-title tag="h3" class="text-wrap">{{ file.productLabel }}</v-card-title>
      <v-card-subtitle class="text-wrap">{{ kindLabel(file.kind) }} · {{ fileSize(file.contentLength) }}</v-card-subtitle>
      <template #append>
        <v-chip :color="STATUS_COLORS[file.status] ?? 'default'" variant="tonal" data-testid="source-file-status">{{ file.status }}</v-chip>
      </template>
    </v-card-item>

    <v-card-text class="flex-grow-1 d-flex flex-column ga-4">
      <div v-if="published || contents || file.warning || file.error" class="d-flex flex-column ga-1">
        <p v-if="published" class="text-body-medium font-weight-medium">{{ published }}</p>
        <p v-if="contents" class="text-body-medium text-medium-emphasis">{{ contents }}</p>
        <p v-if="file.warning || file.error" class="text-body-small text-warning">{{ file.warning ?? file.error }}</p>
      </div>
      <p v-if="!file.counts" class="text-body-medium text-medium-emphasis">Not loaded yet.</p>

      <section v-if="file.counts">
        <h4 class="text-title-small mb-1">How it was cleaned</h4>
        <ul v-if="cleaning.length" class="text-body-medium ps-4" data-testid="source-cleaning">
          <li v-for="line in cleaning" :key="line">{{ line }}</li>
        </ul>
        <p v-else class="text-body-medium">Nothing needed cleaning.</p>
      </section>

      <section v-if="file.counts">
        <h4 class="text-title-small mb-1">Set aside</h4>
        <p v-if="!file.rejects.length" class="text-body-medium" data-testid="source-no-rejects">Nothing was set aside.</p>
        <v-table v-else density="compact">
          <thead>
            <tr>
              <th>Why</th>
              <th>How many</th>
              <th>First example</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="reject in file.rejects" :key="reject.reason" data-testid="source-reject">
              <td class="text-body-medium">{{ reject.description }}</td>
              <td class="text-body-medium">{{ wholeNumber(reject.count) }}</td>
              <td class="text-body-small"><code class="text-break">{{ reject.example ?? '—' }}</code></td>
            </tr>
          </tbody>
        </v-table>
      </section>

      <section>
        <h4 class="text-title-small mb-1">Trace it</h4>
        <v-table density="compact">
          <tbody>
            <tr>
              <td>Index (snapshot) date</td>
              <td>{{ shortDate(file.dates.indexDate) }}</td>
            </tr>
            <tr>
              <td>File's own "last updated"</td>
              <td>{{ shortDate(file.dates.fileLastUpdatedOn) }}</td>
            </tr>
            <tr>
              <td>Server's last modified</td>
              <td>{{ shortDate(file.dates.httpLastModified) }}</td>
            </tr>
          </tbody>
        </v-table>
        <div class="d-flex flex-column ga-1 mt-2">
          <code v-if="file.sha256" class="text-body-small text-break">SHA-256 {{ file.sha256 }}</code>
          <code v-if="file.eTag" class="text-body-small text-break">ETag {{ file.eTag }}</code>
          <p v-if="timing" class="text-body-small text-medium-emphasis">{{ timing }}</p>
        </div>
      </section>
    </v-card-text>

    <v-card-actions>
      <v-btn :href="file.url" target="_blank" rel="noopener" variant="text" color="primary" :append-icon="mdiOpenInNew">Open the file</v-btn>
    </v-card-actions>
  </v-card>
</template>
