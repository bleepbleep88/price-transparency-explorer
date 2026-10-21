<script setup lang="ts">
import { computed } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import { wholeNumber } from '@/lib/format'
import type { SourceFile } from '../api/types'
import { cleaningLines, contentsLine, datesLine, fileSize, kindLabel, namesLine, publishedLine, timingLine } from '../lib/sourceText'

// One file's details: what it held, how it was cleaned, what was set aside and how to trace it.
const props = defineProps<{ file: SourceFile }>()

const published = computed(() => publishedLine(props.file))
const contents = computed(() => contentsLine(props.file))
const cleaning = computed(() => cleaningLines(props.file))
const timing = computed(() => timingLine(props.file))
const names = computed(() => namesLine(props.file))
</script>

<template>
  <section class="file" data-testid="source-file">
    <h4 class="file-title">
      {{ file.productLabel }} · {{ kindLabel(file.kind) }}
      <span class="file-size">{{ fileSize(file.contentLength) }}</span>
    </h4>

    <p v-if="file.warning || file.error" class="problem">{{ file.warning ?? file.error }}</p>
    <p v-if="!file.counts">Not loaded yet.</p>
    <template v-else>
      <p v-if="published">{{ published }}</p>
      <p v-if="contents">{{ contents }}</p>
      <ul v-if="cleaning.length" class="lines" data-testid="source-cleaning">
        <li v-for="line in cleaning" :key="line">{{ line }}</li>
      </ul>
      <p v-if="names" data-testid="source-names">{{ names }}</p>
      <p v-if="!file.rejects.length" data-testid="source-no-rejects">Nothing was set aside.</p>
      <v-table v-else density="compact" class="rejects">
        <thead>
          <tr>
            <th>Set aside</th>
            <th class="text-end">How many</th>
            <th>First example</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="reject in file.rejects" :key="reject.reason" data-testid="source-reject">
            <td>{{ reject.description }}</td>
            <td class="text-end">{{ wholeNumber(reject.count) }}</td>
            <td><code class="trace">{{ reject.example ?? '—' }}</code></td>
          </tr>
        </tbody>
      </v-table>
    </template>

    <p>{{ datesLine(file) }}</p>
    <p v-if="timing">{{ timing }}</p>
    <code v-if="file.sha256" class="trace">SHA-256 {{ file.sha256 }}</code>
    <code v-if="file.eTag" class="trace">ETag {{ file.eTag }}</code>
    <a :href="file.url" target="_blank" rel="noopener" class="file-link">
      Open the file <v-icon :icon="mdiOpenInNew" size="16" aria-hidden="true" />
    </a>
  </section>
</template>

<style scoped>
.file {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.file-title {
  font-size: 1rem;
  font-weight: 600;
}

.file-size {
  font-weight: 400;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.problem {
  color: rgb(var(--v-theme-error));
}

.lines {
  padding-left: 16px;
}

.rejects {
  background: transparent;
  font-size: 0.875rem;
}

.trace {
  font-size: 0.875rem;
  overflow-wrap: anywhere;
}

.file-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  align-self: flex-start;
  color: rgb(var(--v-theme-primary));
}
</style>
