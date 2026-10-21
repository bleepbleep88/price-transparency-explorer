<script setup lang="ts">
import { computed } from 'vue'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import TrendLine from '@/features/history/components/TrendLine.vue'
import type { RateTrendResult } from '@/features/history/api/types'
import { trendFor } from '@/features/history/lib/trend'
import { shortDate } from '@/lib/format'
import { LIKE_FOR_LIKE } from '@/lib/plainNumbers'
import { codeName, ownName } from '@/lib/plainNames'
import type { DirectorySource, RateGroup, RateSearchResult } from '../api/types'
import { groupName, pinsProvider, qualityLines } from '../lib/lenses'
import { pricingNotes } from '../lib/pricing'
import { TECHNICAL_HINTS } from '../lib/answer'
import { directorySourceNote } from '../lib/whoLine'

// Everything behind the answer, for anyone who wants it: the code and its category, how it's priced, the clean-up,
// the bill picked in full, and its price over the monthly files.
const props = defineProps<{
  result: RateSearchResult
  group: RateGroup | null
  trend?: RateTrendResult | null
  directory?: DirectorySource | null
}>()

const code = computed(() => props.result.code)
const category = computed(() => {
  const classification = code.value?.classification
  if (!classification) return null
  const repeats = classification.subcategory?.toLowerCase() === classification.category.toLowerCase()
  const parts = [classification.category, repeats ? null : classification.subcategory, classification.family]
  return parts.filter(Boolean).join(' · ')
})
const notes = computed(() => pricingNotes(props.result))
const cleanup = computed(() => qualityLines(props.result.quality))
const hints = computed(() => props.result.hints.filter((hint) => TECHNICAL_HINTS.has(hint.kind)))
const groupTrend = computed(() => (props.group ? trendFor(props.trend, props.group.key) : null))
</script>

<template>
  <DetailsPanel testid="search-details">
    <dl class="facts">
      <div v-if="code">
        <dt>Code</dt>
        <dd data-testid="detail-code">
          {{ code.codeType }} {{ code.code }}<template v-if="codeName(code.code, code.name)"> · {{ codeName(code.code, code.name) }}</template>
          <template v-if="code.descriptionSource === 'cms_hcpcs'"> (name from CMS's HCPCS list)</template>
          <template v-else-if="code.descriptionSource === 'rbcs' && !ownName(code.code)"> (CMS's category name)</template>
        </dd>
      </div>
      <div v-if="category">
        <dt>CMS category</dt>
        <dd data-testid="category-chip">{{ category }}</dd>
      </div>
      <div v-if="code">
        <dt>Priced</dt>
        <dd>{{ code.category.unit }}</dd>
      </div>
      <div v-if="code?.foundVia.length">
        <dt>Found as</dt>
        <dd>{{ code.foundVia.join('; ') }}</dd>
      </div>
      <div v-if="group">
        <dt>This bill</dt>
        <dd data-testid="detail-group">{{ groupName(group) }}</dd>
      </div>
      <div>
        <dt>File</dt>
        <dd>The plan's {{ shortDate(result.snapshot.indexDate) }} file. Each price compares {{ LIKE_FOR_LIKE.toLowerCase() }}.</dd>
      </div>
      <div>
        <dt>Clean-up</dt>
        <dd>{{ cleanup.join(' ') }}</dd>
      </div>
      <div v-if="directory">
        <dt>Names and places</dt>
        <dd>{{ directorySourceNote(directory) }}</dd>
      </div>
    </dl>

    <div v-if="hints.length" class="notes">
      <h3 class="notes-title">How the plan lists it</h3>
      <p v-for="(hint, index) in hints" :key="index" :data-testid="`hint-${hint.kind}`">{{ hint.message }}</p>
    </div>

    <div v-if="notes.length" class="notes" data-testid="pricing-notes">
      <h3 class="notes-title">How it's priced</h3>
      <div v-for="note in notes" :key="note.key" :data-testid="`pricing-note-${note.key}`">
        <p class="font-weight-medium">{{ note.title }}</p>
        <p>{{ note.text }}</p>
      </div>
    </div>

    <div v-if="groupTrend && group" class="trend">
      <h3 class="notes-title">Price over time</h3>
      <TrendLine :trend="groupTrend" :selected-id="trend?.selected.snapshotId ?? null" :show-provider="pinsProvider(result)" />
    </div>
  </DetailsPanel>
</template>

<style scoped>
.facts {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 8px 16px;
}

.facts > div {
  display: contents;
}

.facts dt {
  font-weight: 600;
}

.facts dd {
  min-width: 0;
  overflow-wrap: anywhere;
}

@media (max-width: 480px) {
  .facts {
    grid-template-columns: 1fr;
  }
}

.notes,
.trend {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.notes-title {
  font-family: var(--v-font-body);
  font-size: 1rem;
  font-weight: 600;
}
</style>
